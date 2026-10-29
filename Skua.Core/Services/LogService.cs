using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using System.Diagnostics;
using System.Text;

namespace Skua.Core.Services;

public class LogService : ObservableRecipient, ILogService, IDisposable
{
    private readonly DebugListener _debugListener;
    private const int MaxLogEntries = 10000;
    private readonly object _debugLock = new();
    private readonly object _scriptLock = new();
    private readonly object _flashLock = new();
    private readonly object _fileLock = new();
    private readonly List<string> _pendingFileLogs = new();
    private readonly Timer _fileFlushTimer;
    private readonly ISettingsService _settingsService;
    private string _accountName = "unknown";
    private string? _lastFilePath;
    private DateTime _lastCleanupDate = DateTime.MinValue;

    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const int KeepDays = 14;

    public LogService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _debugListener = new DebugListener(this);
        Trace.Listeners.Add(_debugListener);
        Messenger.Register<LogService, FlashErrorMessage>(this, LogFlashError);
        _fileFlushTimer = new Timer(_ => FlushFileLogs(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public void SetAccountName(string? accountName)
    {
        lock (_fileLock)
            _accountName = string.IsNullOrWhiteSpace(accountName) ? "unknown" : SanitizeFileName(accountName);
    }

    private void LogFlashError(LogService recipient, FlashErrorMessage message)
    {
        recipient.FlashLog($"{message.Function} Args[{message.Args.Length}] {(message.Args.Length > 0 ? $"= {{{string.Join(",", message.Args.Select(a => a?.ToString()))}}}" : string.Empty)}");
    }

    private readonly List<string> _debugLogs = new();
    private readonly List<string> _scriptLogs = new();
    private readonly List<string> _flashLogs = new();

    public void DebugLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Contains("PlatformImpl is null"))
            return;

        lock (_debugLock)
        {
            if (_debugLogs.Count >= MaxLogEntries)
                _debugLogs.RemoveAt(0);
            _debugLogs.Add(message);
        }
        Messenger.Send(new LogsChangedMessage(LogType.Debug));
        Messenger.Send(new AddLogMessage(LogType.Debug, message));
        QueueFileLog("Debug", message);
    }

    public void FlashLog(string? message)
    {
        if (message is null)
            return;
        lock (_flashLock)
        {
            if (_flashLogs.Count >= MaxLogEntries)
                _flashLogs.RemoveAt(0);
            _flashLogs.Add(message);
        }
        Messenger.Send(new LogsChangedMessage(LogType.Flash));
        Messenger.Send(new AddLogMessage(LogType.Flash, message));
    }

    public void ScriptLog(string? message)
    {
        if (message is null)
            return;
        lock (_scriptLock)
        {
            if (_scriptLogs.Count >= MaxLogEntries)
                _scriptLogs.RemoveAt(0);
            _scriptLogs.Add(message);
        }
        Messenger.Send(new LogsChangedMessage(LogType.Script));
        Messenger.Send(new AddLogMessage(LogType.Script, message));
        QueueFileLog("Script", message);
    }

    public void ClearLog(LogType logType)
    {
        switch (logType)
        {
            case LogType.Debug:
                lock (_debugLock)
                    _debugLogs.Clear();
                break;

            case LogType.Script:
                lock (_scriptLock)
                    _scriptLogs.Clear();
                break;

            case LogType.Flash:
                lock (_flashLock)
                    _flashLogs.Clear();
                break;
        }
        Messenger.Send(new LogsChangedMessage(logType));
    }

    public List<string> GetLogs(LogType logType)
    {
        return logType switch
        {
            LogType.Debug => new List<string>(_debugLogs),
            LogType.Script => new List<string>(_scriptLogs),
            LogType.Flash => new List<string>(_flashLogs),
            _ => new()
        };
    }

    private void QueueFileLog(string category, string message)
    {
        if (!_settingsService.Get<bool>("SaveLogsToFile"))
            return;

        string line = $"{DateTime.Now:HH:mm:ss.fff} [{category}] {message}";
        lock (_fileLock)
            _pendingFileLogs.Add(line);
    }

    private void FlushFileLogs()
    {
        List<string> pending;
        lock (_fileLock)
        {
            if (_pendingFileLogs.Count == 0 || !_settingsService.Get<bool>("SaveLogsToFile"))
                return;
            pending = new List<string>(_pendingFileLogs);
            _pendingFileLogs.Clear();
        }

        try
        {
            string logsDirectory = Path.Combine(ClientFileSources.VibeSkuaDIR, "logs");
            Directory.CreateDirectory(logsDirectory);
            CleanupOldFiles(logsDirectory);

            lock (_fileLock)
            {
                string? previous = null;
                int repeatCount = 0;
                foreach (string line in pending)
                {
                    if (line == previous)
                    {
                        repeatCount++;
                        continue;
                    }

                    WriteCollapsedLine(logsDirectory, previous, repeatCount);
                    previous = line;
                    repeatCount = 0;
                }

                WriteCollapsedLine(logsDirectory, previous, repeatCount);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Bonxie persistent logging failed: {ex.Message}");
        }
    }

    private void WriteCollapsedLine(string directory, string? line, int repeatCount)
    {
        if (line is null)
            return;

        string output = line + Environment.NewLine;
        if (repeatCount > 0)
            output += $"{DateTime.Now:HH:mm:ss.fff} (previous line repeated {repeatCount} more times){Environment.NewLine}";

        string path = GetLogPath(directory, output);
        File.AppendAllText(path, output, Encoding.UTF8);
        _lastFilePath = path;
    }

    private string GetLogPath(string directory, string output)
    {
        string baseName = $"{SanitizeFileName(_accountName)}_{DateTime.Now:yyyy-MM-dd}";
        string path = _lastFilePath is not null && Path.GetFileNameWithoutExtension(_lastFilePath).StartsWith(baseName + "_", StringComparison.Ordinal)
            ? _lastFilePath
            : Path.Combine(directory, baseName + ".log");

        if (File.Exists(path) && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(output) > MaxFileBytes)
        {
            for (int part = 1; ; part++)
            {
                string candidate = Path.Combine(directory, $"{baseName}_{part:000}.log");
                if (!File.Exists(candidate) || new FileInfo(candidate).Length + Encoding.UTF8.GetByteCount(output) <= MaxFileBytes)
                {
                    path = candidate;
                    break;
                }
            }
        }

        return path;
    }

    private void CleanupOldFiles(string directory)
    {
        DateTime today = DateTime.Today;
        if (_lastCleanupDate == today)
            return;
        _lastCleanupDate = today;

        DateTime cutoff = today.AddDays(-KeepDays);
        foreach (string path in Directory.EnumerateFiles(directory, "*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(path) < cutoff)
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private static string SanitizeFileName(string value)
    {
        StringBuilder builder = new();
        foreach (char c in value)
            builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _fileFlushTimer.Dispose();
            FlushFileLogs();

            // Remove the trace listener
            if (_debugListener != null)
            {
                Trace.Listeners.Remove(_debugListener);
            }

            // Unregister from messenger
            Messenger.UnregisterAll(this);

            // Clear log collections
            _debugLogs.Clear();
            _scriptLogs.Clear();
            _flashLogs.Clear();
        }

        _disposed = true;
    }

    ~LogService()
    {
        Dispose(false);
    }
}

public class DebugListener : TraceListener
{
    private readonly ILogService _logService;

    public DebugListener(ILogService logService)
    {
        _logService = logService;
    }

    public override void Write(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Contains("PlatformImpl is null"))
            return;
        _logService.DebugLog(message);
    }

    public override void WriteLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Contains("PlatformImpl is null"))
            return;
        _logService.DebugLog(message);
    }
}

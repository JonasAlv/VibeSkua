using Skua.Core.Interfaces;
using System;
using System.Xml.Linq;

namespace Skua.App.Avalonia.Flash;

#if IS_LINUX
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using global::Avalonia.Threading;
using Skua.App.Avalonia.Controls;
using Skua.Core.Flash;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Utils;

public class FlashUtil : IFlashUtil, IRestartableGamePlayer
{
    public void RestartGamePlayer()
    {
        InitializeFlash();
    }

    public bool CanRestartClient => false;

    public void RestartClient(bool runScript)
    {
        RestartGamePlayer();
    }
    private readonly IMessenger _messenger;
    private readonly Lazy<IScriptManager> _lazyManager;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<JsonElement>> _pendingCalls = new();
    private readonly object _writeLock = new();
    private readonly Channel<(string Name, object[] Args)> _eventChannel;

    private Socket? _listenSocket;
    private Socket? _clientSocket;
    private StreamWriter? _writer;
    private Process? _mugetsuProcess;
    private string? _socketPath;
    private CancellationTokenSource? _cts;
    private ulong _nextCallId;
    private bool _resizeSubscribed;

    public FlashUtil(IMessenger messenger, Lazy<IScriptManager> lazyManager)
    {
        _messenger = messenger;
        _lazyManager = lazyManager;
        _eventChannel = Channel.CreateUnbounded<(string Name, object[] Args)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _ = Task.Run(ProcessFlashEventsAsync);
    }

    public static void PreloadSwf()
    {
    }

    public event FlashCallHandler? FlashCall;

    private async Task ProcessFlashEventsAsync()
    {
        await foreach (var (name, args) in _eventChannel.Reader.ReadAllAsync())
        {
            try
            {
                FlashCall?.Invoke(name, args);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Flash CallHandler exception: {ex.Message}");
            }
        }
    }

    public void InitializeFlash()
    {
        CleanupMugetsu();

        if (!_resizeSubscribed)
        {
            _resizeSubscribed = true;
            WinFormsFlashHost.HostResized += OnHostResized;
            WinFormsFlashHost.KeyForwarded += OnKeyForwarded;
            WinFormsFlashHost.TextForwarded += OnTextForwarded;
            WinFormsFlashHost.FocusForwarded += OnFocusForwarded;
            WinFormsFlashHost.ClearKeysForwarded += OnClearKeysForwarded;
        }

        _cts = new CancellationTokenSource();
        _socketPath = Path.Combine(Path.GetTempPath(), $"vibeskua-flash-{Environment.ProcessId}-{Guid.NewGuid():N}.sock");
        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch { }
        }

        _listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listenSocket.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listenSocket.Listen(1);

        CancellationToken token = _cts.Token;
        _ = Task.Run(() => AcceptAndReadLoopAsync(_listenSocket, token), token);

        string? mugetsuBin = ResolveMugetsuBinary();
        if (string.IsNullOrEmpty(mugetsuBin))
        {
            Trace.WriteLine("Mugetsu binary not found! Set MUGETSU_BIN environment variable or place Mugetsu next to the executable.");
            return;
        }

        string swfPath = Path.Combine(AppContext.BaseDirectory, "skua.swf");
        if (!File.Exists(swfPath))
            swfPath = Path.Combine(ClientFileSources.SkuaDIR, "skua.swf");

        ProcessStartInfo psi = new()
        {
            FileName = mugetsuBin,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(swfPath);
        psi.Environment["MUGETSU_SKUA_SOCKET"] = _socketPath;
        psi.Environment.Remove("WAYLAND_DISPLAY");
        psi.Environment.Remove("WAYLAND_SOCKET");

        if (WinFormsFlashHost.HostXid != 0)
            psi.Environment["MUGETSU_PARENT_XID"] = WinFormsFlashHost.HostXid.ToString(CultureInfo.InvariantCulture);

        try
        {
            _mugetsuProcess = Process.Start(psi);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to start Mugetsu ({mugetsuBin}): {ex.Message}");
        }
    }

    private static string? ResolveMugetsuBinary()
    {
        string? envBin = Environment.GetEnvironmentVariable("MUGETSU_BIN");
        if (!string.IsNullOrWhiteSpace(envBin) && File.Exists(envBin))
            return envBin;

        string localBin = Path.Combine(AppContext.BaseDirectory, "Mugetsu");
        if (File.Exists(localBin))
            return localBin;

        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "mugetsu", "source", "build", "out", "Mugetsu");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        string fallback = "/home/me/aqw-projects/mugetsu/source/build/out/Mugetsu";
        if (File.Exists(fallback))
            return fallback;

        return null;
    }

    private async Task AcceptAndReadLoopAsync(Socket listenSocket, CancellationToken token)
    {
        try
        {
            Socket client = await listenSocket.AcceptAsync(token);
            _clientSocket = client;

            using NetworkStream netStream = new(client, ownsSocket: false);
            _writer = new StreamWriter(netStream, new UTF8Encoding(false)) { AutoFlush = true };
            using StreamReader reader = new(netStream, Encoding.UTF8);

            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line is null)
                    break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(line);
                    JsonElement root = doc.RootElement;
                    string? msgType = root.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() : null;

                    switch (msgType)
                    {
                        case "ready":
                            if (root.TryGetProperty("xid", out JsonElement xidEl) && xidEl.TryGetUInt64(out ulong xid))
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    WinFormsFlashHost.NotifyChildXidReady(xid);
                                });
                            }
                            break;

                        case "mouse_down":
                            Dispatcher.UIThread.Post(WinFormsFlashHost.NotifyGameClicked);
                            break;

                        case "reply":
                            if (root.TryGetProperty("id", out JsonElement idEl) && idEl.TryGetUInt64(out ulong id))
                            {
                                JsonElement valClone = root.TryGetProperty("value", out JsonElement valEl)
                                    ? valEl.Clone()
                                    : default;
                                if (_pendingCalls.TryRemove(id, out var tcs))
                                    tcs.TrySetResult(valClone);
                            }
                            break;

                        case "event":
                            string? funcName = root.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : null;
                            if (!string.IsNullOrEmpty(funcName))
                            {
                                object[] parsedArgs = Array.Empty<object>();
                                if (root.TryGetProperty("args", out JsonElement argsEl) && argsEl.ValueKind == JsonValueKind.Array)
                                {
                                    parsedArgs = argsEl.EnumerateArray().Select(FromJsonElement).ToArray();
                                }
                                _eventChannel.Writer.TryWrite((funcName, parsedArgs));
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Error parsing Skua IPC message: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Skua IPC loop ended: {ex.Message}");
        }
        finally
        {
            foreach (var kvp in _pendingCalls)
            {
                if (_pendingCalls.TryRemove(kvp.Key, out var tcs))
                    tcs.TrySetCanceled();
            }
        }
    }

    private static object FromJsonElement(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.Number => el.TryGetInt32(out int i) ? i : el.TryGetSingle(out float f) ? f : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null!,
            JsonValueKind.Array => el.EnumerateArray().Select(FromJsonElement).ToArray(),
            JsonValueKind.Object => ToExpando(el),
            _ => el.GetRawText()
        };
    }

    private static object ToExpando(JsonElement el)
    {
        IDictionary<string, object?> expando = new ExpandoObject();
        foreach (JsonProperty prop in el.EnumerateObject())
            expando[prop.Name] = FromJsonElement(prop.Value);
        return expando;
    }

    private void OnHostResized(int width, int height)
    {
        SendJsonLine(new
        {
            cmd = "resize",
            width,
            height
        });
    }

    private void OnKeyForwarded(bool down, string physical, string logical, bool ctrl, bool shift, bool alt)
    {
        SendJsonLine(new
        {
            cmd = "key",
            down,
            physical,
            logical,
            ctrl,
            shift,
            alt
        });
    }

    private void OnTextForwarded(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            SendJsonLine(new
            {
                cmd = "text",
                text
            });
        }
    }

    private void OnFocusForwarded(bool focused)
    {
        SendJsonLine(new
        {
            cmd = "focus",
            focused
        });
    }

    private void OnClearKeysForwarded()
    {
        SendJsonLine(new
        {
            cmd = "clear_keys"
        });
    }

    private bool SendJsonLine(object payload)
    {
        lock (_writeLock)
        {
            if (_writer is null)
                return false;
            try
            {
                string json = JsonSerializer.Serialize(payload);
                _writer.WriteLine(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public string Call(string function, params object[] args)
    {
        return Call<string>(function, args);
    }

    public T Call<T>(string function, params object[] args)
    {
        try
        {
            object? o = Call(function, typeof(T), args);
            return o is T value ? value : GetDefaultValue<T>();
        }
        catch
        {
            return GetDefaultValue<T>();
        }
    }

    private static T GetDefaultValue<T>()
    {
        object? defaultValue = DefaultProvider.GetDefault<T>(typeof(T));
        return defaultValue is T typed ? typed : default!;
    }

    public object Call(string function, Type type, params object[] args)
    {
        if (function == "headless")
        {
            bool on = args.Length > 0 && Convert.ToBoolean(args[0]);
            SendJsonLine(new { cmd = "headless", on });
            return default!;
        }

        if (_lazyManager.Value.ShouldExit && Thread.CurrentThread.Name == "Script Thread")
            _lazyManager.Value.ScriptCts?.Token.ThrowIfCancellationRequested();

        try
        {
            ulong id = Interlocked.Increment(ref _nextCallId);
            TaskCompletionSource<JsonElement> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingCalls[id] = tcs;

            if (!SendJsonLine(new { id, cmd = "call", name = function, args }))
            {
                _pendingCalls.TryRemove(id, out _);
                return default!;
            }

            if (!tcs.Task.Wait(TimeSpan.FromSeconds(10)))
            {
                _pendingCalls.TryRemove(id, out _);
                return default!;
            }

            JsonElement el = tcs.Task.GetAwaiter().GetResult();
            if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return default!;

            string strVal = el.ValueKind switch
            {
                JsonValueKind.String => string.IsNullOrEmpty(el.GetString()) ? string.Empty : new XText(el.GetString()!).ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => el.GetRawText()
            };

            if (string.IsNullOrWhiteSpace(strVal))
                return default!;

            if (type == typeof(string))
                return strVal;

            if (strVal == "null" || strVal == "undefined")
                return default!;

            object? value = Convert.ChangeType(strVal, type, CultureInfo.InvariantCulture);
            return value ?? default!;
        }
        catch (Exception e)
        {
            if (function != "isKicked")
                _messenger.Send<FlashErrorMessage>(new(e, function, args));
            return default!;
        }
    }

    public object FromFlashXml(XElement el)
    {
        switch (el.Name.ToString())
        {
            case "number":
                return int.TryParse(el.Value, out int i) ? i : float.TryParse(el.Value, out float f) ? f : 0;
            case "true":
                return true;
            case "false":
                return false;
            case "null":
                return null!;
            case "array":
                return el.Elements().Select(FromFlashXml).ToArray();
            case "object":
                dynamic d = new ExpandoObject();
                el.Elements().ForEach(e => d[e.Attribute("id")!.Value] = FromFlashXml(e.Elements().First()));
                return d;
            default:
                return el.Value;
        }
    }

    public IFlashObject<T> CreateFlashObject<T>(string path)
    {
        return new FlashObject<T>(Call<int>("lnkCreate", path), this);
    }

    private void CleanupMugetsu()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;

        lock (_writeLock)
        {
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }

        try { _clientSocket?.Dispose(); } catch { }
        _clientSocket = null;

        try { _listenSocket?.Dispose(); } catch { }
        _listenSocket = null;

        if (!string.IsNullOrEmpty(_socketPath) && File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch { }
        }
        _socketPath = null;

        if (_mugetsuProcess is not null)
        {
            try
            {
                if (!_mugetsuProcess.HasExited)
                    _mugetsuProcess.Kill(entireProcessTree: true);
            }
            catch { }
            try { _mugetsuProcess.Dispose(); } catch { }
            _mugetsuProcess = null;
        }
    }

    public void Dispose()
    {
        WinFormsFlashHost.HostResized -= OnHostResized;
        WinFormsFlashHost.KeyForwarded -= OnKeyForwarded;
        WinFormsFlashHost.TextForwarded -= OnTextForwarded;
        WinFormsFlashHost.FocusForwarded -= OnFocusForwarded;
        WinFormsFlashHost.ClearKeysForwarded -= OnClearKeysForwarded;
        CleanupMugetsu();
    }
}
#endif

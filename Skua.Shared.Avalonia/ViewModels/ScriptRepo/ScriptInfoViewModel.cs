using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Models.GitHub;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace Skua.Shared.Avalonia.ViewModels.ScriptRepo;

public partial class ScriptInfoViewModel : ObservableObject
{
    public ScriptInfoViewModel(ScriptInfo info, string? localFileOverride = null, bool isCustom = false, bool? isDownloaded = null, int? localSize = null, bool isPinned = false)
    {
        Info = info;
        _localFileOverride = localFileOverride;
        IsCustom = isCustom;
        _downloaded = isDownloaded ?? info.CachedDownloaded ?? DownloadedFileExists;
        _localSize = localSize ?? info.CachedLocalSize ?? (Downloaded ? (int)new FileInfo(LocalFile).Length : 0);
        _isPinned = isPinned;
        
        UpdateSearchIndex();
    }

    public ScriptInfo Info { get; }
    private readonly string? _localFileOverride;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Outdated), nameof(StatusText), nameof(StatusColor), nameof(StatusBackground))]
    private int _localSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinIcon), nameof(PinColor), nameof(PinToolTip))]
    private bool _isPinned;

    partial void OnIsPinnedChanged(bool value)
    {
        UpdateSearchIndex();
    }

    public string PinIcon => IsPinned ? "Pin" : "PinOutline";
    public string PinColor => IsPinned ? "#F59E0B" : "#9CA3AF";
    public string PinToolTip => IsPinned ? "Unpin script" : "Pin script to top";
    public string ScriptKey => (IsCustom ? LocalFile : FilePath).Replace('\\', '/').Trim();

    private string _searchIndex = string.Empty;
    public string SearchIndex
    {
        get => _searchIndex;
        private set => SetProperty(ref _searchIndex, value);
    }

    private void UpdateSearchIndex()
    {
        string tagsStr = Info.Tags != null && Info.Tags.Length > 0 ? string.Join(" ", Info.Tags) : string.Empty;
        SearchIndex = $"{Info.Name} {DisplayPath} {tagsStr} {(IsPinned ? "pinned" : string.Empty)}".ToLowerInvariant();
    }
    public string FileName => Info.Name;
    public int Size => Info.Size;
    public string LocalFile => _localFileOverride ?? Info.LocalFile;
    public string FilePath => Info.FilePath;
    public bool IsCustom { get; }
    public string DisplayPath => IsCustom ? LocalFile : ScriptPath;

    private bool DownloadedFileExists => File.Exists(LocalFile);
    public string ScriptPath => Path.Combine(ClientFileSources.SkuaScriptsDIR, FilePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
    public string FormattedCreationDate => Info.CreationDate.HasValue && Info.CreationDate.Value > DateTime.MinValue ? Info.CreationDate.Value.ToString("MMM dd, yyyy") : "Unknown";

    private ObservableCollection<string>? _infoTags;
    public ObservableCollection<string> InfoTags => _infoTags ??= new(Info.Tags ?? Array.Empty<string>());

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Outdated), nameof(StatusText), nameof(StatusColor), nameof(StatusBackground))]
    private bool _downloaded;

    public bool Outdated => !IsCustom && Downloaded && (_localSize != Info.Size || Info.Outdated);

    public string FormattedSize => Size > 1024 * 1024
        ? $"{Size / (1024.0 * 1024.0):F1} MB"
        : Size > 1024
            ? $"{Size / 1024.0:F1} KB"
            : $"{Size} B";

    public string StatusText => IsCustom ? "Local" : Outdated ? "Update Available" : Downloaded ? "Downloaded" : "Available";
    public string StatusColor => IsCustom ? "#A78BFA" : Outdated ? "#FBBF24" : Downloaded ? "#34D399" : "#9CA3AF";
    public string StatusBackground => IsCustom ? "#2E1065" : Outdated ? "#451A03" : Downloaded ? "#064E3B" : "#1F2937";

    [RelayCommand]
    private void LoadScript(ScriptInfoViewModel selectedScript)
    {
        if (selectedScript is null || !selectedScript.Downloaded)
            return;

        StrongReferenceMessenger.Default.Send<LoadScriptMessage, int>(new(selectedScript.LocalFile), (int)MessageChannels.ScriptStatus);
    }

    [RelayCommand]
    private void QueueScript(ScriptInfoViewModel selectedScript)
    {
        if (selectedScript is null || !selectedScript.Downloaded)
            return;

        StrongReferenceMessenger.Default.Send<QueueScriptMessage, int>(new(selectedScript.LocalFile), (int)MessageChannels.ScriptStatus);
    }

    [RelayCommand]
    private void StartScript(ScriptInfoViewModel selectedScript)
    {
        if (selectedScript is null || !selectedScript.Downloaded)
            return;

        StrongReferenceMessenger.Default.Send<StartScriptMessage, int>(new(selectedScript.LocalFile), (int)MessageChannels.ScriptStatus);
    }

    public override string ToString()
    {
        return FileName;
    }
}

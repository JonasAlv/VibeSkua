using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace Skua.App.Avalonia.ViewModels.CoreBotsOptions;

public partial class CoreBotsViewModel : BotControlViewModelBase
{
    public CoreBotsViewModel(List<TabItemViewModel> tabs, IScriptPlayer player, IDialogService dialogService)
        : base("CoreBots Options", 800, 650)
    {
        CoreBotsTabs = tabs;
        _selectedTab = CoreBotsTabs[0];
        _player = player;
        _dialogService = dialogService;
        if (ClassEnhance is CBOClassEnhanceViewModel classEnhance)
            classEnhance.SaveTarget = () => SaveInternal(showDialog: false);
    }

    private CBOClassEnhanceViewModel? ClassEnhance => CoreBotsTabs.Select(t => t.Content).OfType<CBOClassEnhanceViewModel>().FirstOrDefault();

    protected override void OnActivated()
    {
        // Always open on the account logged in here
        SetProfileSilently(_player.Username);
        Load();
    }

    protected override void OnDeactivated()
    {
        // Auto-save when closing the window
        if (!string.IsNullOrEmpty(ProfileName))
        {
            SaveInternal(showDialog: false);
        }
        base.OnDeactivated();
    }

    private readonly IScriptPlayer _player;
    private readonly IDialogService _dialogService;
    private bool _switchingProfile;

    [ObservableProperty]
    private TabItemViewModel _selectedTab;

    [ObservableProperty]
    private string _currentPlayer = string.Empty;

    /// <summary>
    /// Accounts whose CBO file can be edited here: the one logged in on this tab and the ones logged in on the other open tabs.
    /// </summary>
    public ObservableCollection<string> Profiles { get; } = new();

    [ObservableProperty]
    private string? _selectedProfile;

    public List<TabItemViewModel> CoreBotsTabs { get; }

    private string ProfileName => string.IsNullOrEmpty(SelectedProfile) ? _player.Username : SelectedProfile;

    // Keeps the edits of the profile being left and shows the chosen one
    partial void OnSelectedProfileChanged(string? oldValue, string? newValue)
    {
        if (_switchingProfile || string.IsNullOrEmpty(newValue))
            return;

        if (!string.IsNullOrEmpty(oldValue))
            SaveInternal(showDialog: false, oldValue);
        LoadProfile(newValue, resetIfMissing: true);
    }

    public void RefreshProfiles() => SetProfileSilently(SelectedProfile);

    private void SetProfileSilently(string? selected)
    {
        _switchingProfile = true;
        try
        {
            Profiles.Clear();
            foreach (string name in ConnectedAccounts.Get(_player.Username))
                Profiles.Add(name);
            // A profile chosen earlier stays selectable even if its tab was closed meanwhile
            if (!string.IsNullOrEmpty(selected) && !Profiles.Contains(selected, StringComparer.OrdinalIgnoreCase))
                Profiles.Add(selected);
            // Nothing chosen: this tab's account, or none while logged out so Load still asks to login
            SelectedProfile = Profiles.FirstOrDefault(p => p.Equals(selected, StringComparison.OrdinalIgnoreCase))
                ?? (string.IsNullOrEmpty(_player.Username) ? null : Profiles.FirstOrDefault());
        }
        finally
        {
            _switchingProfile = false;
        }
    }

    /// <summary>
    /// Opens the Class Enhance tab on <paramref name="className"/> of <paramref name="profile"/> (Loadouts Manager).
    /// </summary>
    public void EditClassEnhance(string profile, string className)
    {
        if (!profile.Equals(ProfileName, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(ProfileName))
                SaveInternal(showDialog: false);
            SetProfileSilently(profile);
            LoadProfile(profile, resetIfMissing: true);
        }

        SelectedTab = CoreBotsTabs.First(t => t.Content is CBOClassEnhanceViewModel);
        ((CBOClassEnhanceViewModel)SelectedTab.Content).SelectClass(className);
    }

    [RelayCommand]
    private void Save()
    {
        SaveInternal(showDialog: true);
    }

    private void SaveInternal(bool showDialog, string? profile = null)
    {
        profile ??= ProfileName;
        if (string.IsNullOrEmpty(profile))
        {
            if (showDialog)
            {
                _dialogService.ShowMessageBox("Login first so that we can fetch your username for the save file", "Save");
                CurrentPlayer = string.Empty;
            }
            return;
        }

        StringBuilder bob = new();
        foreach (TabItemViewModel tab in CoreBotsTabs)
        {
            if (tab.Content is IManageCBOptions cbo)
                cbo.Save(bob);
        }

        string optionsDir = ClientFileSources.SkuaOptionsDIR;
        if (!Directory.Exists(optionsDir))
            Directory.CreateDirectory(optionsDir);

        string filePath = Path.Combine(optionsDir, $"CBO_Storage({profile}).txt");
        File.WriteAllText(filePath, bob.ToString());

        if (showDialog)
        {
            _dialogService.ShowMessageBox($@"Saved to \options\CBO_Storage({profile}).txt", "Save Successful!");
        }
    }

    [RelayCommand]
    private void Load()
    {
        if (string.IsNullOrEmpty(ProfileName))
        {
            _dialogService.ShowMessageBox("Login first so that we can fetch your username to load the options file.", "Load");
            CurrentPlayer = string.Empty;
            return;
        }

        RefreshProfiles();
        LoadProfile(ProfileName, resetIfMissing: false);
    }

    // Always read from disk: another tab may have saved this profile since it was last shown
    private void LoadProfile(string profile, bool resetIfMissing)
    {
        CurrentPlayer = profile;
        if (ClassEnhance is CBOClassEnhanceViewModel classEnhance)
            classEnhance.TargetAccount = profile;

        Dictionary<string, string>? values = ReadProfile(profile);
        // Don't show the previous account's values under a profile that has no file yet
        if (values != null || resetIfMissing)
            SetValues(values ?? new());
    }

    /// <returns>The saved options of <paramref name="profile"/>, or null if it has no CBO file.</returns>
    public static Dictionary<string, string>? ReadProfile(string profile)
    {
        string filePath = Path.Combine(ClientFileSources.SkuaOptionsDIR, $"CBO_Storage({profile}).txt");
        if (!File.Exists(filePath) && Directory.Exists(ClientFileSources.LegacySkuaDIR))
        {
            string legacyPath = Path.Combine(ClientFileSources.LegacySkuaDIR, "options", $"CBO_Storage({profile}).txt");
            if (File.Exists(legacyPath))
                filePath = legacyPath;
        }

        if (!File.Exists(filePath))
            return null;

        Dictionary<string, string> optionsDict = new();
        foreach (string option in File.ReadAllLines(filePath))
        {
            ReadOnlySpan<string> value = option.Split(_separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (value.Length == 2)
                optionsDict.TryAdd(value[0], value[1]);
        }
        return optionsDict;
    }

    private void SetValues(Dictionary<string, string> options)
    {
        foreach (TabItemViewModel tab in CoreBotsTabs)
        {
            if (tab.Content is IManageCBOptions setable)
                setable.SetValues(options);
        }
    }

    private const char _separator = ':';
}

internal interface IManageCBOptions
{
    StringBuilder Save(StringBuilder builder);

    void SetValues(Dictionary<string, string> values);
}

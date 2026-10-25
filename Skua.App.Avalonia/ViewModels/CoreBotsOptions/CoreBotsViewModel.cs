using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Skua.App.Avalonia.ViewModels.CoreBotsOptions;

public partial class CoreBotsViewModel : BotControlViewModelBase
{
    public CoreBotsViewModel(List<TabItemViewModel> tabs, IScriptPlayer player, IDialogService dialogService)
        : base("CoreBots Options")
    {
        CoreBotsTabs = tabs;
        _selectedTab = CoreBotsTabs[0];
        _player = player;
        _dialogService = dialogService;
    }

    protected override void OnActivated()
    {
        Load();
    }

    protected override void OnDeactivated()
    {
        // Auto-save when closing the window
        if (!string.IsNullOrEmpty(_player.Username))
        {
            SaveInternal(showDialog: false);
        }
        base.OnDeactivated();
    }

    private readonly IScriptPlayer _player;
    private readonly IDialogService _dialogService;
    private Dictionary<string, Dictionary<string, string>> _readValues = new();

    [ObservableProperty]
    private TabItemViewModel _selectedTab;

    [ObservableProperty]
    private string _currentPlayer = string.Empty;

    public List<TabItemViewModel> CoreBotsTabs { get; }

    [RelayCommand]
    private void Save()
    {
        SaveInternal(showDialog: true);
    }

    private void SaveInternal(bool showDialog)
    {
        if (string.IsNullOrEmpty(_player.Username))
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

        string filePath = Path.Combine(optionsDir, $"CBO_Storage({_player.Username}).txt");
        File.WriteAllText(filePath, bob.ToString());

        if (showDialog)
        {
            _dialogService.ShowMessageBox($"Saved to {Path.Combine(ClientFileSources.SkuaOptionsDIR, $"CBO_Storage({_player.Username}).txt")}", "Save Successful!");
        }
        _readValues[_player.Username] = ReadValues(File.ReadAllLines(filePath));
    }

    [RelayCommand]
    private void Load()
    {
        if (string.IsNullOrEmpty(_player.Username))
        {
            _dialogService.ShowMessageBox("Login first so that we can fetch your username to load the options file.", "Load");
            CurrentPlayer = string.Empty;
            return;
        }

        CurrentPlayer = _player.Username;
        if (_readValues.ContainsKey(_player.Username))
        {
            SetValues(_readValues[_player.Username]);
            return;
        }

        string filePath = Path.Combine(ClientFileSources.SkuaOptionsDIR, $"CBO_Storage({_player.Username}).txt");
        if (!File.Exists(filePath) && Directory.Exists(ClientFileSources.LegacySkuaDIR))
        {
            string legacyPath = Path.Combine(ClientFileSources.LegacySkuaDIR, "options", $"CBO_Storage({_player.Username}).txt");
            if (File.Exists(legacyPath))
                filePath = legacyPath;
        }

        if (!File.Exists(filePath))
            return;

        Dictionary<string, string> optionsDict = ReadValues(File.ReadAllLines(filePath));

        SetValues(optionsDict);

        _readValues.Add(_player.Username, optionsDict);
    }

    private Dictionary<string, string> ReadValues(IEnumerable<string> lines)
    {
        Dictionary<string, string> optionsDict = new();
        foreach (string option in lines)
        {
            ReadOnlySpan<string> value = option.Split(_separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (value.Length == 2)
                optionsDict.Add(value[0], value[1]);
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

    private readonly char _separator = ':';
}

internal interface IManageCBOptions
{
    StringBuilder Save(StringBuilder builder);

    void SetValues(Dictionary<string, string> values);
}
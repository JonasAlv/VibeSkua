using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Skua.Core.Interfaces;
using Skua.Core.Models.Servers;
using Skua.Shared.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels.Options;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace Skua.App.Avalonia.ViewModels.Options;

public class GameOptionsViewModel : BotControlViewModelBase
{
    private static readonly string[] DefaultAQWServers =
    {
        "Artix", "Twilly", "Galanoth", "Gravelyn", "Safiria", "Sir Ver", "Sepulchure", "Yorumi", "Alteon", "Espada", "Yokai", "Zorbot"
    };

    private readonly IScriptServers _servers;
    private readonly IScriptOption _options;

    public GameOptionsViewModel(List<DisplayOptionItemViewModelBase> gameOptions, IScriptServers servers, IScriptOption options)
        : base("Game Options", 640, 720)
    {
        _servers = servers;
        _options = options;
        _selectedServer = !string.IsNullOrWhiteSpace(options.ReloginServer) ? options.ReloginServer : "Twilly";
        GameOptions = gameOptions;
        FilteredGameOptions = new ObservableCollection<DisplayOptionItemViewModelBase>(SortOptions(gameOptions));
        ResetOptionsCommand = new RelayCommand(_options.Reset);
        ResetDefaultOptionsCommand = new RelayCommand(_options.ResetToDefault);
        SaveOptionsCommand = new RelayCommand(_options.Save);
        BuildGroups();
    }

    protected override void OnActivated()
    {
        Messenger.Register<GameOptionsViewModel, PropertyChangedMessage<List<Server>>>(this, ServersChanged);
        Messenger.Register<GameOptionsViewModel, PropertyChangedMessage<string>>(this, OptionServerChanged);
        OnPropertyChanged(nameof(ServersList));
    }

    public List<DisplayOptionItemViewModelBase> GameOptions { get; }
    public ObservableCollection<DisplayOptionItemViewModelBase> FilteredGameOptions { get; }
    public List<OptionGroupViewModel> OptionGroups { get; private set; } = new();

    private string _searchText = string.Empty;
    private string _selectedCategory = "All";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplySearch();
                OnPropertyChanged(nameof(FilteredGroups));
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
                OnPropertyChanged(nameof(FilteredGroups));
        }
    }

    public List<string> Categories { get; } = new()
    {
        "All",
        "Performance & Visuals",
        "Combat & Targeting",
        "Drops & Looting",
        "Auto-Relogin & Network",
        "Gameplay & Character"
    };

    public IEnumerable<OptionGroupViewModel> FilteredGroups
    {
        get
        {
            IEnumerable<OptionGroupViewModel> groups = OptionGroups;
            if (_selectedCategory != "All")
            {
                groups = groups.Where(g => g.Title.Equals(_selectedCategory, StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrWhiteSpace(_searchText))
                return groups;

            return groups.Select(g => new OptionGroupViewModel
            {
                Title = g.Title,
                Icon = g.Icon,
                Description = g.Description,
                Items = g.Items.Where(i => i.Content.Contains(_searchText, StringComparison.OrdinalIgnoreCase) || 
                                           (i.Description?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ?? false)).ToList()
            }).Where(g => g.Items.Count > 0);
        }
    }

    private void BuildGroups()
    {
        var visuals = GameOptions.Where(i =>
            i.Content.Contains("Lag", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Animation", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Player", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("FPS", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Collision", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Reload Map", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Upgrade", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Staff", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("FX", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var combat = GameOptions.Where(i =>
            i.Content.Contains("Aggro", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Target", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Attack", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Skill", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Range", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Magnet", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Rest", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Hunt", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Timing", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var drops = GameOptions.Where(i =>
            i.Content.Contains("Drop", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Loot", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Reject", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Accept", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var relogin = GameOptions.Where(i =>
            i.Content.Contains("Relogin", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Login", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Retry", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Server", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var used = new HashSet<DisplayOptionItemViewModelBase>(visuals.Concat(combat).Concat(drops).Concat(relogin));
        var gameplay = GameOptions.Where(i => !used.Contains(i)).ToList();

        var streamerMode = gameplay.FirstOrDefault(i => i.Content.Equals("Streamer Mode", StringComparison.OrdinalIgnoreCase));
        var streamerName = gameplay.FirstOrDefault(i => i.Content.Equals("Streamer Name", StringComparison.OrdinalIgnoreCase));
        if (streamerMode != null && streamerName != null)
        {
            gameplay.Remove(streamerName);
            int idx = gameplay.IndexOf(streamerMode);
            gameplay.Insert(idx + 1, streamerName);
        }

        OptionGroups = new List<OptionGroupViewModel>
        {
            new()
            {
                Title = "Performance & Visuals",
                Icon = "Speedometer",
                Description = "Lag killer, animation toggles, player visibility, and frame rate",
                Items = visuals
            },
            new()
            {
                Title = "Combat & Targeting",
                Icon = "SwordCross",
                Description = "Monster provocation, infinite range, skill timing, and auto target",
                Items = combat
            },
            new()
            {
                Title = "Drops & Looting",
                Icon = "PackageVariantClosed",
                Description = "AC items pickup, auto drops acceptance, and drop delay",
                Items = drops
            },
            new()
            {
                Title = "Auto-Relogin & Network",
                Icon = "ServerNetwork",
                Description = "Automatic server reconnection, retry limits, and login timeouts",
                Items = relogin
            },
            new()
            {
                Title = "Gameplay & Character",
                Icon = "AccountCogOutline",
                Description = "Walk speed, custom name/guild tags, and miscellaneous preferences",
                Items = gameplay
            }
        };
    }

    public List<string> ServersList
    {
        get
        {
            HashSet<string> serverNames = new(StringComparer.OrdinalIgnoreCase);

            if (_servers.CachedServers != null && _servers.CachedServers.Count > 0)
            {
                foreach (Server server in _servers.CachedServers)
                {
                    if (!string.IsNullOrWhiteSpace(server.Name))
                        serverNames.Add(server.Name);
                }
            }

            if (serverNames.Count == 0 && _servers.ServerList != null && _servers.ServerList.Count > 0)
            {
                foreach (Server server in _servers.ServerList)
                {
                    if (!string.IsNullOrWhiteSpace(server.Name))
                        serverNames.Add(server.Name);
                }
            }

            if (serverNames.Count == 0)
            {
                foreach (string name in DefaultAQWServers)
                    serverNames.Add(name);
            }

            return serverNames.OrderBy(s => s).ToList();
        }
    }

    private string? _selectedServer;

    public string? SelectedServer
    {
        get => _selectedServer ?? _options.ReloginServer ?? "Twilly";
        set
        {
            if (SetProperty(ref _selectedServer, value) && value is not null && _options.ReloginServer != value)
                _options.ReloginServer = value;
        }
    }

    public IRelayCommand ResetOptionsCommand { get; }
    public IRelayCommand ResetDefaultOptionsCommand { get; }
    public IRelayCommand SaveOptionsCommand { get; }

    private void ServersChanged(GameOptionsViewModel recipient, PropertyChangedMessage<List<Server>> message)
    {
        if (message.PropertyName == nameof(IScriptServers.CachedServers))
            recipient.OnPropertyChanged(nameof(recipient.ServersList));
    }

    private void OptionServerChanged(GameOptionsViewModel recipient, PropertyChangedMessage<string> message)
    {
        if (message.PropertyName == nameof(IScriptOption.ReloginServer) && message.NewValue != recipient.SelectedServer)
            recipient.SelectedServer = message.NewValue;
    }

    private void ApplySearch()
    {
        IEnumerable<DisplayOptionItemViewModelBase> filtered = GameOptions.Where(MatchesSearch);
        List<DisplayOptionItemViewModelBase> sorted = SortOptions(filtered).ToList();
        FilteredGameOptions.Clear();
        foreach (DisplayOptionItemViewModelBase option in sorted)
            FilteredGameOptions.Add(option);
    }

    private bool MatchesSearch(DisplayOptionItemViewModelBase option)
    {
        string trimmedSearch = SearchText.Trim();

        if (string.IsNullOrEmpty(trimmedSearch))
            return true;

        return option.Content?.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IEnumerable<DisplayOptionItemViewModelBase> SortOptions(IEnumerable<DisplayOptionItemViewModelBase> options)
    {
        return options
            .OrderBy(o => o.Tag)
            .ThenBy(o => o.Content, StringComparer.OrdinalIgnoreCase);
    }
}

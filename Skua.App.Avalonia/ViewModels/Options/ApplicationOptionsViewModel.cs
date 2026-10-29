using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Shared.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Skua.App.Avalonia.ViewModels.Options;

public class OptionGroupViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = "Cog";
    public string Description { get; set; } = string.Empty;
    public List<DisplayOptionItemViewModelBase> Items { get; set; } = new();
}

public partial class ApplicationOptionsViewModel : BotControlViewModelBase
{
    private string _searchText = string.Empty;
    private string _selectedCategory = "All";

    public ApplicationOptionsViewModel(List<DisplayOptionItemViewModelBase> appOptions)
        : base("Application Options", 580, 650)
    {
        ApplicationOptions = appOptions;
        BuildGroups();
    }

    public List<DisplayOptionItemViewModelBase> ApplicationOptions { get; }
    public List<OptionGroupViewModel> OptionGroups { get; private set; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                OnPropertyChanged(nameof(FilteredGroups));
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

    public List<string> Categories { get; } = new() { "All", "General & Performance", "Auto Updates", "Webhooks" };

    public IEnumerable<OptionGroupViewModel> FilteredGroups
    {
        get
        {
            IEnumerable<OptionGroupViewModel> groups = OptionGroups;
            if (_selectedCategory != "All")
            {
                groups = groups.Where(g => g.Title.Contains(_selectedCategory, StringComparison.OrdinalIgnoreCase));
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
        var generalItems = ApplicationOptions.Where(i =>
            i.Content.Contains("Username", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Logs", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Frame-rate", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Relogin", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Flash Cache", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var updateItems = ApplicationOptions.Where(i =>
            i.Content.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Updates", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var webhookItems = ApplicationOptions.Where(i =>
            i.Content.Contains("Webhook", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
            i.Content.Contains("Nexus", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var otherItems = ApplicationOptions.Except(generalItems).Except(updateItems).Except(webhookItems).ToList();

        OptionGroups = new List<OptionGroupViewModel>
        {
            new()
            {
                Title = "General & Performance",
                Icon = "ApplicationCogOutline",
                Description = "Client interface, animation frame rates, relogin timings, and cache",
                Items = generalItems.Concat(otherItems).ToList()
            },
            new()
            {
                Title = "Auto Updates & Sync",
                Icon = "CloudSyncOutline",
                Description = "Automatic synchronization for bot scripts, skill sets, and item database",
                Items = updateItems
            },
            new()
            {
                Title = "Webhooks & Alerts",
                Icon = "Webhook",
                Description = "Remote script notifications, crash alerts, relog pings, and rare drop alerts for Discord & Skua Nexus",
                Items = webhookItems
            }
        };
    }
}

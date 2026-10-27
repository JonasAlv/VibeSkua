using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.Utils;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.ViewModels.Runtime;

public sealed partial class JunkItemsViewModel : BotControlViewModelBase
{
    private readonly IScriptInventory _inventory;
    private readonly IScriptBank _bank;
    private readonly IJunkService _junkService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settings;
    private string _searchText = string.Empty;

    public JunkItemsViewModel(
        IScriptInventory inventory,
        IScriptBank bank,
        IJunkService junkService,
        IDialogService dialogService,
        ISettingsService settings)
       : base("Junk Items", 550, 500)
    {
        _inventory = inventory;
        _bank = bank;
        _junkService = junkService;
        _dialogService = dialogService;
        _settings = settings;
        Items = new RangedObservableCollection<JunkItemEntry>();
        FilteredItems = new ObservableCollection<JunkItemEntry>();
        _skipSellWarning = _settings.Get("JunkSkipSellWarning", false);
    }

    public RangedObservableCollection<JunkItemEntry> Items { get; }
    public ObservableCollection<JunkItemEntry> FilteredItems { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplySearch();
        }
    }

    [ObservableProperty]
    private bool _skipSellWarning;

    public int TotalJunk => Items.Count(i => i.IsJunk);

    public int SelectedCount => Items.Count(i => i.IsSelected);

    // Drives the select-all check box above the list: true when every shown item is selected, false when none
    // is, null (indeterminate) in between. Checking it selects the shown items; unchecking clears the selection.
    public bool? SelectionState
    {
        get
        {
            int selected = SelectedCount;
            if (selected == 0)
                return false;
            return FilteredItems.Count > 0 && FilteredItems.All(i => i.IsSelected) ? true : null;
        }
        set
        {
            if (value == true)
                SelectAll();
            else
                ClearSelection();
        }
    }

    private void Entry_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JunkItemEntry.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectionState));
        }
        else if (e.PropertyName == nameof(JunkItemEntry.IsJunk))
        {
            OnPropertyChanged(nameof(TotalJunk));
        }
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        _ = Refresh();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        _junkService.Load();

        var junkIds = _junkService.JunkItems
            .GroupBy(j => j.ID)
            .Select(g => g.Key)
            .ToHashSet();

        var previousStates = Items
            .ToList()
            .ToDictionary<JunkItemEntry, (int ID, string Category), bool>(
                e => (e.ID, e.Category),
                e => e.IsSelected);

        List<JunkItemEntry> entries = await Task.Run(() =>
        {
            if (!_bank.Loaded)
                _bank.Load();

            List<InventoryItem> inventoryItems = _inventory.Items.ToList();
            List<InventoryItem> bankItems = _bank.Items.ToList();

            IEnumerable<InventoryItem> allItems = inventoryItems.Concat(bankItems);

            return allItems
                .GroupBy(i => i.ID)
                .Select(g => g.First())
                .OrderBy(i => i.Name)
                .Select(item => new JunkItemEntry(item.ID, item.Name, item.Category.ToString(), junkIds.Contains(item.ID)))
                .ToList();
        });

        foreach (JunkItemEntry entry in entries)
        {
            if (previousStates.TryGetValue((entry.ID, entry.Category), out bool isSelected))
                entry.IsSelected = isSelected;
        }

        foreach (JunkItemEntry old in Items)
            old.PropertyChanged -= Entry_PropertyChanged;
        foreach (JunkItemEntry entry in entries)
            entry.PropertyChanged += Entry_PropertyChanged;

        Items.ReplaceRange(entries);
        ApplySearch();
        OnPropertyChanged(nameof(TotalJunk));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionState));
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in FilteredItems)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Items)
            item.IsSelected = false;
    }

    [RelayCommand]
    private void ToggleJunk()
    {
        var selected = Items.Where(e => e.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        bool allJunk = selected.All(e => e.IsJunk);
        foreach (var item in selected)
            item.IsJunk = !allJunk;

        PersistFromEntries(Items.ToList());
    }

    [RelayCommand]
    private void MarkAsJunk()
    {
        if (Items.Count == 0)
            return;

        var updated = Items.ToList();
        foreach (JunkItemEntry entry in updated.Where(e => e.IsSelected))
        {
            entry.IsJunk = true;
        }

        PersistFromEntries(updated);
    }

    [RelayCommand]
    private void UnmarkAsJunk()
    {
        if (Items.Count == 0)
            return;

        var updated = Items.ToList();
        foreach (JunkItemEntry entry in updated.Where(e => e.IsSelected))
        {
            entry.IsJunk = false;
            entry.IsSelected = false;
        }

        PersistFromEntries(updated);
    }

    [RelayCommand]
    private void UnmarkAllJunk()
    {
        if (Items.Count == 0)
            return;

        var updated = Items.ToList();
        foreach (JunkItemEntry entry in updated)
        {
            entry.IsJunk = false;
            entry.IsSelected = false;
        }

        PersistFromEntries(updated);
    }

    [RelayCommand]
    private async Task SellSelected()
    {
        var selected = Items.Where(e => e.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        if (!SkipSellWarning)
        {
            bool? confirm = _dialogService.ShowMessageBox(
                $"This will sell {selected.Count} selected items from your inventory. This cannot be undone. Continue?",
                "Sell Selected",
                yesAndNo: true);

            if (confirm != true)
                return;
        }

        foreach (var item in selected)
            item.IsJunk = true;
        PersistFromEntries(Items.ToList());

        _junkService.SellAllJunk();
        await Refresh();
    }

    [RelayCommand]
    private async Task SellAllJunk()
    {
        if (Items.Count == 0)
            return;

        if (TotalJunk == 0)
            return;

        if (!SkipSellWarning)
        {
            bool? confirm = _dialogService.ShowMessageBox(
                "This will sell all items marked as junk in your inventory. This cannot be undone. Continue?",
                "Sell All Junk",
                yesAndNo: true);

            if (confirm != true)
                return;
        }

        _junkService.SellAllJunk();
        await Refresh();
    }

    partial void OnSkipSellWarningChanged(bool value)
    {
        if (value)
        {
            bool? confirm = _dialogService.ShowMessageBox(
                "Are you sure you want to skip the confirmation when selling all junk?",
                "Skip Sell Warning",
                yesAndNo: true);

            if (confirm != true)
            {
                _skipSellWarning = false;
                OnPropertyChanged(nameof(SkipSellWarning));
                return;
            }
        }

        _settings.Set("JunkSkipSellWarning", value);
    }

    private void PersistFromEntries(List<JunkItemEntry> entries)
    {
        var configs = entries
            .Where(e => e.IsJunk)
            .Select(e => new JunkItemConfig
            {
                ID = e.ID,
                Name = e.Name,
                Category = e.Category,
                Meta = string.Empty
            })
            .ToList();

        _junkService.SetJunk(configs);
        ApplySearch();
        OnPropertyChanged(nameof(TotalJunk));
    }

    private void ApplySearch()
    {
        string filter = SearchText.Trim();
        FilteredItems.Clear();
        foreach (JunkItemEntry item in Items)
        {
            if (string.IsNullOrEmpty(filter) ||
                item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.ID.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredItems.Add(item);
            }
        }
        OnPropertyChanged(nameof(SelectionState));
    }
}

public sealed partial class JunkItemEntry : ObservableObject
{
    public JunkItemEntry(int id, string name, string category, bool isJunk)
    {
        ID = id;
        Name = name;
        Category = category;
        _isJunk = isJunk;
    }

    public int ID { get; }
    public string Name { get; }
    public string Category { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isJunk;

    public string Display => $"[{ID}] {Name} ({Category})";
}
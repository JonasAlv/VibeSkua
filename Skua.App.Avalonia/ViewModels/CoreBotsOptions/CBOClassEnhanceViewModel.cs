using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Scripts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.ViewModels.CoreBotsOptions;

/// <summary>
/// Per-class enhancement profiles. Every class starts with the CoreAdvanced recommendation (ClassEnhancePresets.json);
/// custom ones are saved to the CBO file as "ClassEnh[class]: Base|WeaponForge|WeaponAwe|Cape|Helm"
/// and replace the built-in library in CoreAdvanced.SmartEnhance.
/// </summary>
public partial class CBOClassEnhanceViewModel : ObservableObject, IManageCBOptions
{
    private const string KeyPrefix = "ClassEnh[";
    private const string DefaultKey = "*";

    public CBOClassEnhanceViewModel(IScriptInventory inventory, IScriptPlayer player, IScriptManager manager, IFileDialogService fileDialog, IDialogService dialogService)
    {
        _inventory = inventory;
        _player = player;
        _manager = manager;
        _fileDialog = fileDialog;
        _dialogService = dialogService;

        AddEntry(new ClassEnhanceEntry(DefaultKey, "Default (classes without a custom profile)", DefaultProfile));
        foreach ((string name, ClassEnhanceProfile preset) in Presets)
            AddEntry(new ClassEnhanceEntry(name, CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name), preset));

        ApplyFilter();
    }

    private static readonly ClassEnhanceProfile DefaultProfile = new("Lucky", "None", "None", "None", "None");

    private static readonly Dictionary<string, ClassEnhanceProfile> Presets = LoadPresets();

    private static Dictionary<string, ClassEnhanceProfile> LoadPresets()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClassEnhancePresets.json");
        return stream is null ? new() : JsonSerializer.Deserialize<Dictionary<string, ClassEnhanceProfile>>(stream) ?? new();
    }

    /// <summary>
    /// The saved profile <see cref="Enhance"/> would use for <paramref name="className"/>: custom profile, then enabled Default, then the class recommendation.
    /// </summary>
    public static string Resolve(Dictionary<string, string>? values, string className)
    {
        string key = className.ToLowerInvariant();
        return Custom(key) ?? Custom(DefaultKey) ?? Serialize(Presets.GetValueOrDefault(key, DefaultProfile));

        string? Custom(string k) => values != null && values.TryGetValue($"{KeyPrefix}{k}]", out string? v) && v.Split('|').Length == 5 ? v : null;
        static string Serialize(ClassEnhanceProfile p) => $"{p.Base}|{p.WeaponForge}|{p.WeaponAwe}|{p.Cape}|{p.Helm}";
    }

    public void SelectClass(string className)
    {
        SearchText = string.Empty;
        OnlyOwned = false;
        SelectedEntry = GetOrAdd(className.ToLowerInvariant());
        ApplyFilter();
    }

    private readonly IScriptInventory _inventory;
    private readonly IScriptPlayer _player;
    private readonly IScriptManager _manager;
    private readonly IFileDialogService _fileDialog;
    private readonly IDialogService _dialogService;
    private readonly List<ClassEnhanceEntry> _entries = new();

    // Same IDs as CoreAdvanced: EnhancementType/CapeSpecial/HelmSpecial are pattern IDs, WeaponSpecial is the proc ID.
    // "Keep" (-1) leaves that item untouched; on WeaponForge it keeps the whole weapon.
    private static readonly Dictionary<string, int> BaseIds = new() { ["Lucky"] = 9, ["Fighter"] = 2, ["Thief"] = 3, ["Hybrid"] = 5, ["Wizard"] = 6, ["Healer"] = 7, ["SpellBreaker"] = 8 };
    private static readonly Dictionary<string, int> WeaponForgeIds = new() { ["None"] = 0, ["Keep"] = -1, ["Forge"] = 1, ["Lacerate"] = 7, ["Smite"] = 8, ["Valiance"] = 9, ["Arcanas_Concerto"] = 10, ["Acheron"] = 11, ["Elysium"] = 12, ["Praxis"] = 13, ["Dauntless"] = 14, ["Ravenous"] = 15 };
    private static readonly Dictionary<string, int> WeaponAweIds = new() { ["None"] = 0, ["Spiral_Carve"] = 2, ["Awe_Blast"] = 3, ["Health_Vamp"] = 4, ["Mana_Vamp"] = 5, ["Powerword_Die"] = 6 };
    private static readonly Dictionary<string, int> CapeIds = new() { ["None"] = 0, ["Keep"] = -1, ["Forge"] = 10, ["Absolution"] = 11, ["Avarice"] = 12, ["Vainglory"] = 24, ["Penitence"] = 29, ["Lament"] = 30 };
    private static readonly Dictionary<string, int> HelmIds = new() { ["None"] = 0, ["Keep"] = -1, ["Forge"] = 10, ["Vim"] = 25, ["Examen"] = 26, ["Anima"] = 28, ["Pneuma"] = 27, ["Hearty"] = 32 };

    public static List<string> BaseEnhancements { get; } = BaseIds.Keys.ToList();
    public static List<string> WeaponForgeSpecials { get; } = WeaponForgeIds.Keys.ToList();
    public static List<string> WeaponAweSpecials { get; } = WeaponAweIds.Keys.ToList();
    public static List<string> CapeSpecials { get; } = CapeIds.Keys.ToList();
    public static List<string> HelmSpecials { get; } = HelmIds.Keys.ToList();

    public ObservableCollection<ClassEnhanceEntry> Entries { get; } = new();

    [ObservableProperty]
    private ClassEnhanceEntry? _selectedEntry;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _onlyOwned;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnOnlyOwnedChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private void RefreshInventory()
    {
        foreach (InventoryItem item in _inventory.Items?.Where(i => i.Category == ItemCategory.Class) ?? Enumerable.Empty<InventoryItem>())
        {
            ClassEnhanceEntry entry = GetOrAdd(item.Name.ToLowerInvariant());
            entry.Display = item.Name;
            entry.Owned = true;
        }
        ApplyFilter();
    }

    /// <summary>
    /// Account whose options are shown (CoreBots profile). Another account is enhanced on the tab logged in to it.
    /// </summary>
    public string? TargetAccount { get; set; }

    /// <summary>
    /// Saves the shown options, so the tab of <see cref="TargetAccount"/> enhances with the current edits.
    /// </summary>
    public Action? SaveTarget { get; set; }

    [RelayCommand]
    private async Task Enhance()
    {
        if (!string.IsNullOrEmpty(TargetAccount) && !TargetAccount.Equals(_player.Username, StringComparison.OrdinalIgnoreCase))
        {
            SaveTarget?.Invoke();
            if (ConnectedAccounts.RequestEnhance(TargetAccount))
                _dialogService.ShowMessageBox($"Enhance sent to the tab of {TargetAccount}.", "Enhance");
            else
                _dialogService.ShowMessageBox($"{TargetAccount} is not logged in on any open tab.", "Enhance");
            return;
        }

        if (_manager.ScriptRunning)
        {
            _dialogService.ShowMessageBox("Stop the running script before enhancing.", "Enhance");
            return;
        }

        string? currentClass = _player.CurrentClass?.Name;
        if (string.IsNullOrEmpty(currentClass))
        {
            _dialogService.ShowMessageBox("Login and equip a class first.", "Enhance");
            return;
        }

        // Same priority as SmartEnhance: custom profile, then enabled Default, then the class recommendation
        ClassEnhanceEntry? classEntry = Find(currentClass.ToLowerInvariant());
        ClassEnhanceEntry entry = classEntry is { Enabled: true } || !_entries[0].Enabled ? classEntry ?? _entries[0] : _entries[0];

        if (!OfferAlreadyEnhancedItems(entry))
            return;

        if (!await Task.Run(CoreAdvancedPatcher.EnsurePatched))
        {
            _dialogService.ShowMessageBox("Could not add class enhance support to Scripts/CoreAdvanced.cs (the file changed or is not writable). Update the scripts and try again.", "Enhance");
            return;
        }

        string scriptPath = Path.Combine(ClientFileSources.SkuaScriptsDIR, "ClassEnhanceTemp.cs");
        File.WriteAllText(scriptPath, $@"//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreAdvanced.cs
using Skua.Core.Interfaces;

public class ClassEnhanceTemp
{{
    public void ScriptMain(IScriptInterface bot)
    {{
        // Map.Name has no room number; CoreBots.Join would turn ""battleon-3"" into an invalid ""battleon-3-<room>"".
        string map = bot.Map.Name;
        new CoreAdvanced().EnhanceEquippedWithProfile(""{entry.Serialize()}"");
        if (!string.IsNullOrEmpty(map) && !string.Equals(bot.Map.Name, map, System.StringComparison.OrdinalIgnoreCase))
            CoreBots.Instance.Join(map);
    }}
}}");
        _manager.SetLoadedScript(scriptPath);
        Exception? error = await _manager.StartScript();
        if (error != null)
            _dialogService.ShowMessageBox($"The enhance script could not start:\n\n{error.Message}", "Enhance");
    }

    /// <summary>
    /// For each equipped weapon/cape/helm missing the profile enhancement, offers to equip an inventory item that already has it.
    /// </summary>
    /// <returns><see langword="false"/> if the user cancelled.</returns>
    private bool OfferAlreadyEnhancedItems(ClassEnhanceEntry profile)
    {
        List<InventoryItem> items = _inventory.Items ?? new();
        foreach (InventoryItem equipped in items.Where(i => i.Equipped && SlotOf(i) is not null).ToList())
        {
            if (HasProfileEnhancement(equipped, profile))
                continue;

            InventoryItem? enhanced = items.FirstOrDefault(i => !i.Equipped && SlotOf(i) == SlotOf(equipped) && HasProfileEnhancement(i, profile));
            if (enhanced is null)
                continue;

            DialogResult result = _dialogService.ShowMessageBox(
                $"Your equipped {SlotOf(equipped)} \"{equipped.Name}\" doesn't have the profile enhancement, but \"{enhanced.Name}\" in your inventory already has it.\n\nEquip \"{enhanced.Name}\" or enhance \"{equipped.Name}\"?",
                "Enhance", $"Equip {enhanced.Name}", $"Enhance {equipped.Name}", "Cancel");

            if (result.Value == 0)
                _inventory.EquipItem(enhanced.ID);
            else if (result.Value != 1)
                return false;
        }
        return true;
    }

    private static string? SlotOf(InventoryItem item) =>
        item.ItemGroup == "Weapon" ? "weapon" : item.Category == ItemCategory.Cape ? "cape" : item.Category == ItemCategory.Helm ? "helm" : null;

    private static bool KeepsSlot(InventoryItem item, ClassEnhanceEntry profile) => SlotOf(item) switch
    {
        "weapon" => profile.WeaponForge == "Keep",
        "cape" => profile.Cape == "Keep",
        "helm" => profile.Helm == "Keep",
        _ => false
    };

    // Mirrors CoreAdvanced.AlreadyHasRequestedEnhancement, ignoring the enhancement level
    private static bool HasProfileEnhancement(InventoryItem item, ClassEnhanceEntry profile)
    {
        if (KeepsSlot(item, profile))
            return true;
        if (item.EnhancementLevel <= 0)
            return false;

        int basePattern = BaseIds[profile.Base];
        return SlotOf(item) switch
        {
            "cape" => item.EnhancementPatternID == (profile.Cape == "None" ? basePattern : CapeIds[profile.Cape]),
            "helm" => item.EnhancementPatternID == (profile.Helm == "None" ? basePattern : HelmIds[profile.Helm]),
            _ => profile.WeaponForge == "None" && profile.WeaponAwe == "None"
                ? item.EnhancementPatternID == basePattern
                : WeaponMatches(WeaponForgeIds[profile.WeaponForge]) || WeaponMatches(WeaponAweIds[profile.WeaponAwe])
        };

        bool WeaponMatches(int proc) => proc > 0 && item.ProcID == proc && (proc > 6 || item.EnhancementPatternID == basePattern);
    }

    [RelayCommand]
    private void Export()
    {
        string? path = _fileDialog.Save("JSON Files (*.json)|*.json");
        if (string.IsNullOrEmpty(path))
            return;

        Dictionary<string, ClassEnhanceProfile> profiles = _entries.Where(e => e.Enabled).ToDictionary(e => e.Key, e => e.ToProfile());
        File.WriteAllText(path, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
    }

    [RelayCommand]
    private void Import()
    {
        string? path = _fileDialog.OpenFile("JSON Files (*.json)|*.json");
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            Dictionary<string, ClassEnhanceProfile>? profiles = JsonSerializer.Deserialize<Dictionary<string, ClassEnhanceProfile>>(File.ReadAllText(path));
            foreach ((string key, ClassEnhanceProfile profile) in profiles ?? new())
                GetOrAdd(key.ToLowerInvariant()).Apply(profile);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Could not import profiles: {ex.Message}", "Import");
        }
    }

    public StringBuilder Save(StringBuilder builder)
    {
        foreach (ClassEnhanceEntry entry in _entries.Where(e => e.Enabled))
            builder.AppendLine($"{KeyPrefix}{entry.Key}]: {entry.Serialize()}");
        return builder;
    }

    public void SetValues(Dictionary<string, string> values)
    {
        foreach (ClassEnhanceEntry entry in _entries)
            entry.Enabled = false;

        foreach ((string key, string value) in values)
        {
            if (!key.StartsWith(KeyPrefix) || !key.EndsWith(']'))
                continue;

            string[] parts = value.Split('|');
            if (parts.Length == 5)
                GetOrAdd(key[KeyPrefix.Length..^1]).Apply(new ClassEnhanceProfile(parts[0], parts[1], parts[2], parts[3], parts[4]));
        }
        ApplyFilter();
    }

    private ClassEnhanceEntry? Find(string key) => _entries.FirstOrDefault(e => e.Key == key);

    private ClassEnhanceEntry GetOrAdd(string key)
    {
        ClassEnhanceEntry? entry = Find(key);
        if (entry is null)
        {
            entry = new ClassEnhanceEntry(key, CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key), DefaultProfile);
            AddEntry(entry);
        }
        return entry;
    }

    private void AddEntry(ClassEnhanceEntry entry)
    {
        entry.PropertyChanged += OnEntryChanged;
        _entries.Add(entry);
    }

    // Only one profile can be active: enabling one disables the others
    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ClassEnhanceEntry.Enabled) || sender is not ClassEnhanceEntry { Enabled: true } enabled)
            return;

        foreach (ClassEnhanceEntry entry in _entries.Where(x => x != enabled && x.Enabled))
            entry.Enabled = false;
    }

    private void ApplyFilter()
    {
        ClassEnhanceEntry? selected = SelectedEntry;
        Entries.Clear();
        foreach (ClassEnhanceEntry entry in _entries
            .Where(e => e.Key == DefaultKey || ((!OnlyOwned || e.Owned) && e.Display.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Key != DefaultKey)
            .ThenBy(e => e.Display, StringComparer.OrdinalIgnoreCase))
            Entries.Add(entry);
        SelectedEntry = selected is not null && Entries.Contains(selected) ? selected : Entries[0];
    }
}

public record ClassEnhanceProfile(string Base, string WeaponForge, string WeaponAwe, string Cape, string Helm);

public partial class ClassEnhanceEntry : ObservableObject
{
    public ClassEnhanceEntry(string key, string display, ClassEnhanceProfile preset)
    {
        Key = key;
        _display = display;
        _preset = preset;
        Load(preset);
    }

    private readonly ClassEnhanceProfile _preset;
    private bool _loading;

    public string Key { get; }

    [ObservableProperty]
    private string _display;

    [ObservableProperty]
    private bool _owned;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private string _base = "Lucky";

    [ObservableProperty]
    private string _weaponForge = "None";

    [ObservableProperty]
    private string _weaponAwe = "None";

    [ObservableProperty]
    private string _cape = "None";

    [ObservableProperty]
    private string _helm = "None";

    // Unchecking a custom profile goes back to the recommended one; editing any slot makes it custom.
    partial void OnEnabledChanged(bool value)
    {
        if (!value)
            Load(_preset);
    }

    partial void OnBaseChanged(string value) => MarkCustom();

    partial void OnWeaponForgeChanged(string value) => MarkCustom();

    partial void OnWeaponAweChanged(string value) => MarkCustom();

    partial void OnCapeChanged(string value) => MarkCustom();

    partial void OnHelmChanged(string value) => MarkCustom();

    private void MarkCustom()
    {
        if (!_loading)
            Enabled = true;
    }

    public ClassEnhanceProfile ToProfile() => new(Base, WeaponForge, WeaponAwe, Cape, Helm);

    public string Serialize() => $"{Base}|{WeaponForge}|{WeaponAwe}|{Cape}|{Helm}";

    public void Apply(ClassEnhanceProfile profile)
    {
        Load(profile);
        Enabled = true;
    }

    private void Load(ClassEnhanceProfile profile)
    {
        _loading = true;
        Base = Pick(CBOClassEnhanceViewModel.BaseEnhancements, profile.Base, "Lucky");
        WeaponForge = Pick(CBOClassEnhanceViewModel.WeaponForgeSpecials, profile.WeaponForge, "None");
        WeaponAwe = Pick(CBOClassEnhanceViewModel.WeaponAweSpecials, profile.WeaponAwe, "None");
        Cape = Pick(CBOClassEnhanceViewModel.CapeSpecials, profile.Cape, "None");
        Helm = Pick(CBOClassEnhanceViewModel.HelmSpecials, profile.Helm, "None");
        _loading = false;

        static string Pick(List<string> options, string value, string fallback) => options.Contains(value) ? value : fallback;
    }
}

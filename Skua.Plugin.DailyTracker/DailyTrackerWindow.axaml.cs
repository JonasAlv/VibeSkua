using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;

namespace Skua.Plugin.DailyTracker;

public partial class DailyTrackerWindow : Window
{
    private readonly IScriptInterface _bot;
    private CancellationTokenSource? _cts;
    private Timer? _refreshTimer;
    private static readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private List<QuestCategory> _categories = [];
    private bool _isRefreshing;

    private record TrackedQuestDef(int Id, string Name, string Category, string ScriptPath);

    private static readonly TrackedQuestDef[] _trackedDefs =
    [
        // Resources & Miscellaneous
        new(2091, "Mine Crafting", "Resources & Miscellaneous", "Dailies/MineCrafting.cs"),
        new(2098, "Hard Core Metals", "Resources & Miscellaneous", "Dailies/HardCoreMetals[Mem].cs"),
        new(802, "Elders' Blood", "Resources & Miscellaneous", "Dailies/EldersBlood.cs"),
        new(803, "Sparrow's Blood", "Resources & Miscellaneous", "Dailies/SparrowsBlood.cs"),
        new(3075, "Doom Member Free Spin", "Resources & Miscellaneous", "Dailies/WheelofDoom.cs"),
        new(3076, "Doom Free Weekly Spin", "Resources & Miscellaneous", "Dailies/WheelofDoom.cs"),
        new(1239, "Free Member Magic Keys", "Resources & Miscellaneous", "Dailies/MonthlyTreasureChestKeys[Mem].cs"),
        new(4069, "Free Daily Boost", "Resources & Miscellaneous", "Dailies/FreeDailyBoost[Mem].cs"),
        new(10047, "A Grain of Dirt", "Resources & Miscellaneous", "Dailies/PearlOfNulgath.cs"),
        new(486, "Shadow Shroud (Daily)", "Resources & Miscellaneous", "Dailies/ShadowShroud.cs"),

        // Classes & Factions
        new(3759, "BeastMaster Challenge", "Classes & Factions", "Dailies/BeastMasterChallenge[Mem].cs"),
        new(3827, "Shadow Shield (Daily)", "Classes & Factions", "Dailies/ShadowScytheGeneral.cs"),
        new(3965, "Glacera Ice Token (Cryomancer)", "Classes & Factions", "Dailies/Cryomancer.cs"),
        new(2219, "Pyromancer", "Classes & Factions", "Dailies/Pyromancer.cs"),
        new(1316, "The Collector", "Classes & Factions", "Dailies/TheCollector.cs"),
        new(3596, "Embrace Your Chaos", "Classes & Factions", "Dailies/DagesScrollFragment.cs"),

        // Lord of Order
        new(7156, "Heart of Servitude", "Lord of Order", "Dailies/LordOfOrder.cs"),
        new(7157, "Spirit of Justice", "Lord of Order", "Dailies/LordOfOrder.cs"),
        new(7158, "Purification of Chaos", "Lord of Order", "Dailies/LordOfOrder.cs"),
        new(7159, "Steadfast Will", "Lord of Order", "Dailies/LordOfOrder.cs"),
        new(7160, "Strike of Order", "Lord of Order", "Dailies/LordOfOrder.cs"),
        new(7161, "Harmony", "Lord of Order", "Dailies/LordOfOrder.cs"),
        new(7162, "Ordinance", "Lord of Order", "Dailies/LordOfOrder.cs"),
        new(7163, "Axiom", "Lord of Order", "Dailies/LordOfOrder.cs"),
        new(7164, "Blessing of Order", "Lord of Order", "Dailies/LordOfOrder.cs"),
        new(7165, "The Final Challenge", "Lord of Order", "Dailies/LordOfOrder.cs"),

        // Ultra Bosses (Daily)
        new(8152, "Ultra Ezrajal", "Ultra Bosses (Daily)", "Ultras/UltraEzrajal.cs"),
        new(8153, "Ultra Warden", "Ultra Bosses (Daily)", "Ultras/UltraWarden.cs"),
        new(8154, "Ultra Engineer", "Ultra Bosses (Daily)", "Ultras/UltraEngineer.cs"),
        new(8245, "Ultra Tyndarius", "Ultra Bosses (Daily)", "Ultras/UltraAvatarTyndarius.cs"),
        new(9852, "Queen Iona", "Ultra Bosses (Daily)", "Ultras/QueenIona.cs"),

        // Ultra Bosses (Weekly)
        new(8300, "Champion Drakath", "Ultra Bosses (Weekly)", "Ultras/ChampionDrakath.cs"),
        new(8397, "Ultra Drago", "Ultra Bosses (Weekly)", "Ultras/UltraDrago.cs"),
        new(8547, "Ultra Dage", "Ultra Bosses (Weekly)", "Ultras/UltraDage.cs"),
        new(8692, "Ultra Nulgath", "Ultra Bosses (Weekly)", "Ultras/UltraNulgath.cs"),
        new(8746, "Ultra Darkon", "Ultra Bosses (Weekly)", "Ultras/UltraDarkon.cs"),
        new(9173, "Ultra Speaker", "Ultra Bosses (Weekly)", "Ultras/UltraSpeaker.cs"),
        new(9803, "Astral Empyrean", "Ultra Bosses (Weekly)", "Ultras/AstralEmpyrean.cs"),
        new(10301, "Ultra Gramiel", "Ultra Bosses (Weekly)", "Ultras/UltraGramiel.cs")
    ];

    public DailyTrackerWindow() : this(null!)
    {
    }

    public DailyTrackerWindow(IScriptInterface bot)
    {
        InitializeComponent();
        _bot = bot;
        Skua.Shared.Avalonia.Services.WindowPlacementHelper.CenterOnActiveScreen(this);
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e) => await InitializeAsync();

    private async Task InitializeAsync()
    {
        if (_bot == null)
            return;

        _cts = new CancellationTokenSource();
        try
        {
            await LoadQuestsAsync();
            RegisterEventHandlers();
            StartLiveUpdates();
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"DailyTracker InitializeAsync error: {ex}");
        }
    }

    private void EnsureAllQuestsLoaded(int[] questIds)
    {
        try
        {
            if (_bot == null || !_bot.Player.LoggedIn)
                return;

            int[] missingIds = questIds.Where(id => !_bot.Quests.Tree.Any(q => q.ID == id)).ToArray();
            if (missingIds.Length == 0)
                return;

            _bot.Quests.Load(missingIds);
            _bot.Wait.ForTrue(() => missingIds.All(id => _bot.Quests.Tree.Any(q => q.ID == id)), null, 6, 200);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Batch quest loading failed: {ex}");
        }
    }

    private async Task LoadQuestsAsync()
    {
        if (_bot == null)
            return;

        int[] questIds = _trackedDefs.Select(d => d.Id).Distinct().ToArray();
        Dictionary<int, bool> doneMap = [];

        if (_bot.Player.LoggedIn)
        {
            doneMap = await Task.Run(() =>
            {
                EnsureAllQuestsLoaded(questIds);
                Dictionary<int, bool> map = [];
                foreach (int id in questIds)
                {
                    try
                    {
                        bool done = _bot.Quests.IsDailyComplete(id);
                        if (!done && id >= 7156 && id <= 7165)
                        {
                            int targetVal = id - 7156 + 1;
                            if (_bot.Flash.CallGameFunction<int>("world.getQuestValue", 327) >= targetVal)
                                done = true;
                        }
                        map[id] = done;
                    }
                    catch
                    {
                        map[id] = false;
                    }
                }
                return map;
            });
        }

        List<QuestItem> questItems = _trackedDefs.Select(d =>
        {
            bool isDone = doneMap.TryGetValue(d.Id, out bool done) && done;
            return new QuestItem(d.Id, d.Name, isDone)
            {
                ScriptName = d.ScriptPath,
                Category = d.Category
            };
        }).ToList();

        _categories = questItems
            .GroupBy(q => q.Category)
            .Select(g => new QuestCategory
            {
                Name = g.Key,
                Quests = g.ToList()
            })
            .ToList();

        Dispatcher.UIThread.Post(() => CategoriesControl.ItemsSource = _categories);
    }

    private void RegisterEventHandlers()
    {
        StrongReferenceMessenger.Default.Register<DailyTrackerWindow, QuestTurninMessage, int>(
            this, (int)MessageChannels.GameEvents, (r, m) => r.OnQuestTurnIn(m));

        StrongReferenceMessenger.Default.Register<DailyTrackerWindow, ScriptStoppedMessage, int>(
            this, (int)MessageChannels.ScriptStatus, (r, m) => r.OnScriptStopped(m));

        StrongReferenceMessenger.Default.Register<DailyTrackerWindow, LoginMessage, int>(
            this, (int)MessageChannels.GameEvents, (r, m) =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000);
                        if (_cts?.IsCancellationRequested != true)
                            await LoadQuestsAsync();
                    }
                    catch { }
                });
            });
    }

    private void OnQuestTurnIn(QuestTurninMessage message)
    {
        int questId = message.QuestID;
        foreach (QuestCategory cat in _categories)
        {
            QuestItem? item = cat.Quests.Find(q => q.ID == questId);
            if (item != null)
            {
                Dispatcher.UIThread.Post(() => item.IsDone = true);
                break;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800);
                if (_cts?.IsCancellationRequested != true)
                    await RefreshQuestStatesAsync();
            }
            catch { }
        });
    }

    private void OnScriptStopped(ScriptStoppedMessage message)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500);
                if (_cts?.IsCancellationRequested != true)
                    await RefreshQuestStatesAsync();
            }
            catch { }
        });
    }

    private async Task RefreshQuestStatesAsync()
    {
        if (_bot == null || !_bot.Player.LoggedIn)
            return;

        if (!await _refreshLock.WaitAsync(0))
            return;

        try
        {
            if (_categories.Count == 0)
            {
                await LoadQuestsAsync();
                return;
            }

            await Task.Run(() =>
            {
                int[] allIds = _categories.SelectMany(c => c.Quests).Select(q => q.ID).Distinct().ToArray();
                EnsureAllQuestsLoaded(allIds);
                foreach (QuestCategory category in _categories)
                {
                    foreach (QuestItem quest in category.Quests)
                    {
                        bool newState;
                        try
                        {
                            newState = _bot.Quests.IsDailyComplete(quest.ID);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.WriteLine($"Quest refresh failed {quest.ID}: {ex}");
                            continue;
                        }

                        if (!newState && quest.ID >= 7156 && quest.ID <= 7165)
                        {
                            try
                            {
                                int targetVal = quest.ID - 7156 + 1;
                                if (_bot.Flash.CallGameFunction<int>("world.getQuestValue", 327) >= targetVal)
                                    newState = true;
                            }
                            catch { }
                        }

                        if (quest.IsDone != newState)
                            Dispatcher.UIThread.Post(() => quest.IsDone = newState);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"RefreshQuestStatesAsync error: {ex}");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void StartLiveUpdates()
    {
        _refreshTimer = new Timer(async _ =>
        {
            try
            {
                if (_cts?.IsCancellationRequested != true)
                    await RefreshQuestStatesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"DailyTracker live update failed: {ex}");
            }
        }, null, _refreshInterval, _refreshInterval);
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        RefreshBtn.IsEnabled = false;
        if (RefreshText != null)
            RefreshText.Text = "Checking...";

        try
        {
            await RefreshQuestStatesAsync();
        }
        finally
        {
            if (RefreshText != null)
                RefreshText.Text = "Refresh";
            RefreshBtn.IsEnabled = true;
            _isRefreshing = false;
        }
    }

    private void AllDailies_Click(object? sender, RoutedEventArgs e)
    {
        StartScript("Dailies/0AllDailies.cs");
    }

    private void StartScript_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not string scriptName || string.IsNullOrWhiteSpace(scriptName)) return;
        StartScript(scriptName);
    }

    private void StartCategory_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        string? categoryName = btn.Tag as string;
        if (categoryName == "Lord of Order")
            StartScript("Dailies/LordOfOrder.cs");
    }

    private string? ResolveScriptPath(string scriptRelative)
    {
        if (string.IsNullOrWhiteSpace(scriptRelative))
            return null;

        string normalized = scriptRelative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        // 1. Direct check in SkuaScriptsDIR
        string fullPath = Path.Combine(ClientFileSources.SkuaScriptsDIR, normalized);
        if (File.Exists(fullPath))
            return fullPath;

        // 2. Direct check in AppContext.BaseDirectory / "Scripts"
        string appPath = Path.Combine(AppContext.BaseDirectory, "Scripts", normalized);
        if (File.Exists(appPath))
            return appPath;

        // 3. Search in SkuaScriptsDIR by filename (e.g. UltraEzrajal.cs or 1UltraEzrajalv3.cs)
        string fileName = Path.GetFileName(normalized);
        if (Directory.Exists(ClientFileSources.SkuaScriptsDIR))
        {
            string? found = Directory.EnumerateFiles(ClientFileSources.SkuaScriptsDIR, fileName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (found != null)
                return found;

            // Also check for v3/v2 variant (e.g. UltraEzrajal.cs -> *UltraEzrajal*.cs)
            string nameWithoutExt = Path.GetFileNameWithoutExtension(normalized);
            string? fuzzyFound = Directory.EnumerateFiles(ClientFileSources.SkuaScriptsDIR, $"*{nameWithoutExt}*.cs", SearchOption.AllDirectories)
                .FirstOrDefault(f => !f.Contains("Cached-Scripts"));
            if (fuzzyFound != null)
                return fuzzyFound;
        }

        return null;
    }

    private void StartScript(string scriptName)
    {
        string? path = ResolveScriptPath(scriptName);
        if (path == null)
        {
            _bot?.Log($"[Daily Tracker] Script not found: {scriptName}");
            return;
        }

        _bot?.Log($"[Daily Tracker] Starting {Path.GetFileName(path)}...");
        Task.Run(async () =>
        {
            try
            {
                if (_bot.Manager.ScriptRunning)
                {
                    _bot.Log($"[Daily Tracker] Stopping running script...");
                    await _bot.Manager.StopScript();
                }

                _bot.Manager.SetLoadedScript(path);
                await _bot.Manager.StartScript();
            }
            catch (Exception ex)
            {
                _bot?.Log($"[Daily Tracker] Failed to start {Path.GetFileName(path)}: {ex.Message}");
            }
        });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        try
        {
            StrongReferenceMessenger.Default.UnregisterAll(this);
        }
        catch { }
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }
}

public class QuestCategory : INotifyPropertyChanged
{
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCategoryScript)); }
    }

    public List<QuestItem> Quests { get; set; } = [];
    public bool HasCategoryScript => Name == "Lord of Order";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class QuestItem : INotifyPropertyChanged
{
    private static readonly IBrush _completedBrush = new SolidColorBrush(Color.Parse("#2E7D32"));
    private static readonly IBrush _incompleteBrush = new SolidColorBrush(Color.Parse("#C62828"));

    public int ID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    private bool _isDone;

    public bool IsDone
    {
        get => _isDone;
        set
        {
            _isDone = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowScript));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private string _scriptName = string.Empty;

    public string ScriptName
    {
        get => _scriptName;
        set
        {
            _scriptName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowScript));
        }
    }

    public bool HasScript => !string.IsNullOrEmpty(ScriptName);
    public bool ShowScript => HasScript && !IsDone && (ID < 7156 || ID > 7165);
    public bool IsLordOfOrder => ID >= 7156 && ID <= 7165;
    public IBrush StatusBrush => IsDone ? _completedBrush : _incompleteBrush;
    public string StatusText => IsDone ? "Completed" : "Incomplete";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public QuestItem(int id, string name, bool isDone)
    {
        ID = id;
        Name = name;
        IsDone = isDone;
    }
}

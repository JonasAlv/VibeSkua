using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Material.Icons;
using Material.Icons.Avalonia;
using Skua.App.Avalonia.Services;
using Skua.App.Avalonia.ViewModels;
using Skua.App.Avalonia.ViewModels.MainMenu;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using Skua.Core.Interfaces.ViewModels;
using System.Threading;
using System.Threading.Tasks;
using Skua.Core.AppStartup;

#if IS_WINDOWS
using Avalonia.Input;
using AxShockwaveFlashObjects;
using System.Runtime.Versioning;
#endif

namespace Skua.App.Avalonia.Views;

public partial class MainWindow : Window
{
#if IS_WINDOWS
    public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData);
    
    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const int WM_NCACTIVATE = 0x0086;

    private SubclassProc? _subclassProc;
#endif
    private const int WM_SKUA_JUMP_MAP = 0x0400 + 449;
    private const int WM_SKUA_SET_OPTION = 0x0400 + 450;
    private const int WM_SKUA_LOAD_SCRIPT = 0x0400 + 453;
    private const int WM_SKUA_ARMY_SCHEDULER = 0x0400 + 454;
    private const int WM_SKUA_ARMY_SCHEDULER_STOP = 0x0400 + 455;
    private const int WM_SKUA_CHECK_LOGIN = 0x0400 + 456;
    private const int WM_SKUA_JUMP_PLAYER = 0x0400 + 457;
    private const int WM_SKUA_LOGIN = 0x0400 + 447;
    private const int WM_SKUA_GRIDVIEW = 0x0400 + 444;
    private const int WM_SKUA_AUTO_ATTACK = 0x0400 + 459;
    private const int WM_SKUA_OPEN_BANK = 0x0400 + 460;

    private static readonly TimeSpan MetricRefreshInterval = TimeSpan.FromSeconds(1);

    private readonly IFlashUtil _flash;
    private readonly IScriptOption _options;
    private readonly SkuaStartupHandler _startup;
    private readonly MainMenuViewModel _mainMenuVm;
    private readonly MainViewModel _mainViewModel;
    private MenuItem? _pluginsMenuItem;
    private DispatcherTimer? _tabTitleSyncTimer;
    private bool _isFlashLoaded;
    private bool _isTitleBarPermanentlyHidden = false;
    private int _preThrottleFPS = -1;
    private string? _preThrottleQuality = null;
    private readonly CancellationTokenSource _ipcCancelCts = new();

    public void HideTitleBar()
    {
        _isTitleBarPermanentlyHidden = true;
        var titleBar = this.FindControl<global::Avalonia.Controls.Control>("TitleBar");
        if (titleBar != null)
        {
            titleBar.IsVisible = false;
        }
        
        this.ExtendClientAreaToDecorationsHint = false;
    }

    public MainWindow()
    {
        InitializeComponent();
        Skua.Shared.Avalonia.Services.WindowResizeHelper.Attach(this);
        _mainViewModel = Ioc.Default.GetRequiredService<MainViewModel>();
        _mainMenuVm = Ioc.Default.GetRequiredService<MainMenuViewModel>();
        _flash = Ioc.Default.GetRequiredService<IFlashUtil>();
        _options = Ioc.Default.GetRequiredService<IScriptOption>();
        _startup = Ioc.Default.GetRequiredService<SkuaStartupHandler>();
        
        DataContext = _mainViewModel;
        MainMenuBar.DataContext = _mainMenuVm;
        ConfigureMainMenu();
        RegisterLifecycleHandlers();
        
        #if IS_WINDOWS
        RegisterMessages();
        #endif
    }

    private void ConfigureMainMenu()
    {
        BuildMainMenu();
        _mainMenuVm.MainMenuItems.CollectionChanged += MainMenuItemsChanged;
        if (_mainMenuVm.Plugins is System.Collections.Specialized.INotifyCollectionChanged notifyPlugins)
        {
            notifyPlugins.CollectionChanged += (s, e) => Dispatcher.UIThread.Post(BuildMainMenu);
        }
    }

    private void MainMenuItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(BuildMainMenu);
    }

    private void BuildMainMenu()
    {
        MainMenuControl.Items.Clear();
        foreach (MainMenuItemViewModel item in _mainMenuVm.MainMenuItems)
            MainMenuControl.Items.Add(CreateMainMenuItem(item, isTopLevel: true));
    }

    private global::Avalonia.Controls.MenuItem CreateMainMenuItem(IMainMenuItemViewModel viewModel, bool isTopLevel = false)
    {
        global::Avalonia.Controls.MenuItem menuItem = new()
        {
            Command = viewModel.Command
        };

        if (isTopLevel)
        {
            menuItem.Classes.Add("top-nav-item");
            MaterialIconKind? iconKind = viewModel.Header switch
            {
                "Scripts" => MaterialIconKind.ScriptTextOutline,
                "Options" => MaterialIconKind.CogOutline,
                "Tools & Helpers" => MaterialIconKind.WrenchOutline,
                "Combat" => MaterialIconKind.SwordCross,
                "Bank" => MaterialIconKind.BankOutline,
                "Diagnostics" => MaterialIconKind.BugOutline,
                "Plugins" => MaterialIconKind.PuzzleOutline,
                _ => null
            };

            if (iconKind.HasValue)
            {
                var icon = new MaterialIcon
                {
                    Kind = iconKind.Value,
                    Width = 14,
                    Height = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                icon[!MaterialIcon.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaTextBrush");

                var text = new TextBlock
                {
                    Text = viewModel.Header,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12
                };
                menuItem.Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { icon, text }
                };
            }
            else
            {
                menuItem.Header = viewModel.Header;
            }
        }
        else
        {
            MaterialIconKind? subIconKind = viewModel.Header switch
            {
                "Script Loader" => MaterialIconKind.ScriptOutline,
                "Scheduler" => MaterialIconKind.CalendarClock,
                "Game" => MaterialIconKind.GamepadVariantOutline,
                "Application" => MaterialIconKind.ApplicationCogOutline,
                "CoreBots" => MaterialIconKind.RobotOutline,
                "Application Themes" => MaterialIconKind.PaletteOutline,
                "HotKeys" => MaterialIconKind.KeyboardOutline,
                "Loadouts Manager" => MaterialIconKind.AccountSwitchOutline,
                "Fast Travel" => MaterialIconKind.CompassOutline,
                "Loader" => MaterialIconKind.DownloadOutline,
                "Grabber" => MaterialIconKind.HandBackRightOutline,
                "Current Drops" => MaterialIconKind.GiftOutline,
                "Junk Items" => MaterialIconKind.DeleteSweepOutline,
                "Skills" => MaterialIconKind.LightningBoltOutline,
                "Stats" => MaterialIconKind.ChartLine,
                "Runtime" => MaterialIconKind.PlayNetworkOutline,
                "Logs" => MaterialIconKind.FileDocumentOutline,
                "Console" => MaterialIconKind.Console,
                "Spammer" => MaterialIconKind.SendClockOutline,
                "Logger" => MaterialIconKind.TextSearch,
                "Interceptor" => MaterialIconKind.ShieldBugOutline,
                _ => null
            };

            if (subIconKind.HasValue)
            {
                var subIcon = new MaterialIcon
                {
                    Kind = subIconKind.Value,
                    Width = 14,
                    Height = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                subIcon[!MaterialIcon.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaMutedTextBrush");
                menuItem.Icon = subIcon;
            }
            menuItem.Header = viewModel.Header;
        }

        if (viewModel.Header == "Plugins")
        {
            foreach (IMainMenuItemViewModel subItem in _mainMenuVm.Plugins)
                menuItem.Items.Add(CreateMainMenuItem(subItem, isTopLevel: false));
        }
        else if (viewModel.SubItems != null && viewModel.SubItems.Any())
        {
            foreach (IMainMenuItemViewModel subItem in viewModel.SubItems)
                menuItem.Items.Add(CreateMainMenuItem(subItem, isTopLevel: false));
        }

        return menuItem;
    }

    #if IS_WINDOWS
    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<MainWindow, FlashChangedMessage<AxShockwaveFlash>>(this, static (r, m) => r.OnFlashControlChanged(m.Flash));
    }
    #endif

    private void RegisterLifecycleHandlers()
    {
        Opened += MainWindow_Opened;
        Closing += (s, e) => App.DisposeTrayIcon();
        Closed += MainWindow_Closed;

        if (_options is System.ComponentModel.INotifyPropertyChanged notifyOptions)
        {
            notifyOptions.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(IScriptOption.HeadlessMode))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        bool isHeadless = _options.HeadlessMode;
                        if (HeadlessOverlay != null) HeadlessOverlay.IsVisible = isHeadless;
                        if (FlashHost != null) FlashHost.IsVisible = !isHeadless && _isFlashLoaded;
                    });
                }
            };
        }
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        _startup.Execute();
        _flash.FlashCall += OnFlashCall;

        try
        {
            var themeService = Ioc.Default.GetService<IThemeService>();
            bool isDark = themeService?.IsDarkTheme ?? true;
            Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this, isDark);
        }
        catch { }
        
#if IS_WINDOWS
        _flash.InitializeFlash();
        if (this.TryGetPlatformHandle()?.Handle is IntPtr hwnd && hwnd != IntPtr.Zero)
        {
            SetWindowText(hwnd, _mainViewModel.Title ?? "Skua");
            _subclassProc = WindowSubclassProc;
            SetWindowSubclass(hwnd, _subclassProc, 1, IntPtr.Zero);
        }
#elif IS_LINUX
        Focusable = true;
        AddHandler(global::Avalonia.Input.InputElement.KeyDownEvent, OnLinuxKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(global::Avalonia.Input.InputElement.KeyUpEvent, OnLinuxKeyUp, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Skua.App.Avalonia.Controls.WinFormsFlashHost.GameClicked -= OnLinuxGameClicked;
        Skua.App.Avalonia.Controls.WinFormsFlashHost.GameClicked += OnLinuxGameClicked;

        if (FlashHost != null)
        {
            FlashHost.IsVisible = !_options.HeadlessMode;
            Dispatcher.UIThread.RunJobs();
            FlashHost.Focus();
        }
        _flash.InitializeFlash();
        TabIpc.StartListener(this, Environment.ProcessId, _ipcCancelCts.Token);
#endif

        _tabTitleSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        string lastWrittenUsername = "";
        _tabTitleSyncTimer.Tick += (s, ev) =>
        {
            try
            {
                var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                if (bot.Player.LoggedIn && !string.IsNullOrWhiteSpace(bot.Player.Username))
                {
                    string username = bot.Player.Username;
                    if (username != lastWrittenUsername)
                    {
                        lastWrittenUsername = username;
                        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VibeSkuaTabs");
                        System.IO.Directory.CreateDirectory(dir);
                        string file = System.IO.Path.Combine(dir, $"{Environment.ProcessId}.txt");
                        System.IO.File.WriteAllText(file, username);
                    }
                }
            }
            catch { }
        };
        _tabTitleSyncTimer.Start();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        App.DisposeTrayIcon();
        _tabTitleSyncTimer?.Stop();
        _ipcCancelCts.Cancel();
        try
        {
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VibeSkuaTabs", $"{Environment.ProcessId}.txt");
            if (System.IO.File.Exists(file))
                System.IO.File.Delete(file);

            string readyFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VibeSkuaTabs", $"ready_{Environment.ProcessId}.txt");
            if (System.IO.File.Exists(readyFile))
                System.IO.File.Delete(readyFile);
        }
        catch { }

        _flash.FlashCall -= OnFlashCall;
        _startup.Dispose();
        StrongReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
    
    #if IS_WINDOWS
    private void OnFlashControlChanged(AxShockwaveFlash flash)
    {
        FlashHost.SetChild(flash);
    }
    #else
    private void OnLinuxGameClicked()
    {
        if (AutoToggle != null)
            AutoToggle.IsChecked = false;
        if (JumpToggle != null)
            JumpToggle.IsChecked = false;
        MainMenuControl?.Close();
        Activate();
        if (FlashHost != null && FlashHost.IsVisible)
            FlashHost.Focus();
        else
            Focus();
    }

    private void OnLinuxKeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox)
            return;

        if (FocusManager?.GetFocusedElement() is Button)
            FlashHost?.Focus();

        ForwardLinuxKeyEvent(true, e);
    }

    private void OnLinuxKeyUp(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox)
            return;

        ForwardLinuxKeyEvent(false, e);
    }

    private static void ForwardLinuxKeyEvent(bool down, global::Avalonia.Input.KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Shift);
        bool alt = e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Alt);

        string physical = e.PhysicalKey != global::Avalonia.Input.PhysicalKey.None
            ? e.PhysicalKey.ToString()
            : e.Key.ToString();

        bool isNumPadDigit = e.Key is >= global::Avalonia.Input.Key.NumPad0 and <= global::Avalonia.Input.Key.NumPad9;
        string? symbol = (!string.IsNullOrEmpty(e.KeySymbol) && e.KeySymbol.Length == 1 && !char.IsControl(e.KeySymbol[0]))
            ? e.KeySymbol
            : TryMapSpecialKeyToSymbol(e.Key);

        string logical;
        if (!string.IsNullOrEmpty(symbol))
        {
            logical = (shift && !isNumPadDigit) ? ApplyShiftToSymbol(symbol) : symbol;
        }
        else
        {
            string keyStr = e.Key.ToString();
            logical = (keyStr.Length == 1 && char.IsLetter(keyStr[0]))
                ? (shift ? keyStr.ToUpperInvariant() : keyStr.ToLowerInvariant())
                : keyStr;
        }

        Skua.App.Avalonia.Controls.WinFormsFlashHost.ForwardKey(down, physical, logical, ctrl, shift, alt);
    }

    private static string? TryMapSpecialKeyToSymbol(global::Avalonia.Input.Key key) => key switch
    {
        global::Avalonia.Input.Key.D0 or global::Avalonia.Input.Key.NumPad0 => "0",
        global::Avalonia.Input.Key.D1 or global::Avalonia.Input.Key.NumPad1 => "1",
        global::Avalonia.Input.Key.D2 or global::Avalonia.Input.Key.NumPad2 => "2",
        global::Avalonia.Input.Key.D3 or global::Avalonia.Input.Key.NumPad3 => "3",
        global::Avalonia.Input.Key.D4 or global::Avalonia.Input.Key.NumPad4 => "4",
        global::Avalonia.Input.Key.D5 or global::Avalonia.Input.Key.NumPad5 => "5",
        global::Avalonia.Input.Key.D6 or global::Avalonia.Input.Key.NumPad6 => "6",
        global::Avalonia.Input.Key.D7 or global::Avalonia.Input.Key.NumPad7 => "7",
        global::Avalonia.Input.Key.D8 or global::Avalonia.Input.Key.NumPad8 => "8",
        global::Avalonia.Input.Key.D9 or global::Avalonia.Input.Key.NumPad9 => "9",
        global::Avalonia.Input.Key.Multiply => "*",
        global::Avalonia.Input.Key.Add => "+",
        global::Avalonia.Input.Key.Subtract => "-",
        global::Avalonia.Input.Key.Divide => "/",
        global::Avalonia.Input.Key.Decimal => ".",
        global::Avalonia.Input.Key.Space => " ",
        global::Avalonia.Input.Key.OemMinus => "-",
        global::Avalonia.Input.Key.OemPlus => "=",
        global::Avalonia.Input.Key.OemOpenBrackets => "[",
        global::Avalonia.Input.Key.OemCloseBrackets => "]",
        global::Avalonia.Input.Key.OemPipe or global::Avalonia.Input.Key.OemBackslash => "\\",
        global::Avalonia.Input.Key.OemSemicolon => ";",
        global::Avalonia.Input.Key.OemQuotes => "'",
        global::Avalonia.Input.Key.OemComma => ",",
        global::Avalonia.Input.Key.OemPeriod => ".",
        global::Avalonia.Input.Key.OemQuestion => "/",
        global::Avalonia.Input.Key.OemTilde => "`",
        _ => null
    };

    private static string ApplyShiftToSymbol(string symbol)
    {
        if (symbol.Length == 1 && char.IsLetter(symbol[0]))
            return symbol.ToUpperInvariant();

        return symbol switch
        {
            "1" => "!",
            "2" => "@",
            "3" => "#",
            "4" => "$",
            "5" => "%",
            "6" => "^",
            "7" => "&",
            "8" => "*",
            "9" => "(",
            "0" => ")",
            "-" => "_",
            "=" => "+",
            "[" => "{",
            "]" => "}",
            "\\" => "|",
            ";" => ":",
            "'" => "\"",
            "," => "<",
            "." => ">",
            "/" => "?",
            "`" => "~",
            _ => symbol
        };
    }
    #endif

    private void OnFlashCall(string function, params object[] args)
    {
        if (function == "loaded" && !_isFlashLoaded)
        {
            _isFlashLoaded = true;
            Dispatcher.UIThread.Post(() =>
            {
                LoadingBar.IsVisible = false;
                FlashHost.IsVisible = !_options.HeadlessMode;
                HeadlessOverlay.IsVisible = _options.HeadlessMode;
                if (FlashHost != null && FlashHost.IsVisible && FocusManager?.GetFocusedElement() is not TextBox)
                    FlashHost.Focus();
                try
                {
                    FlashHost?.SetScaleMode(false);
                    _flash.Call("setGameObject", "stage.scaleMode", "showAll");
                }
                catch { }
            });
        }
    }

    public IntPtr HandleIpcMessage(uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == WM_SKUA_GRIDVIEW)
        {
            Dispatcher.UIThread.Post(() =>
            {
                bool isGrid = wParam.ToInt32() == 1;
                
                var topNav = this.FindControl<global::Avalonia.Controls.Control>("TopNavBorder");
                if (topNav != null)
                    topNav.IsVisible = !isGrid;
                
                var titleBar = this.FindControl<global::Avalonia.Controls.Control>("TitleBar");
                if (titleBar != null && !_isTitleBarPermanentlyHidden)
                    titleBar.IsVisible = !isGrid;
                
                this.MinWidth = 0;
                this.MinHeight = 0;

                if (FlashHost != null)
                    FlashHost.SetScaleMode(false);

                try
                {
                    _flash.Call("setGameObject", "stage.scaleMode", "showAll");
                }
                catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_LOGIN)
        {
            Task.Run(() => 
            {
                var options = Ioc.Default.GetRequiredService<IScriptOption>();
                var servers = Ioc.Default.GetRequiredService<IScriptServers>();
                string targetServer = string.IsNullOrWhiteSpace(options.ReloginServer) ? "Twilly" : options.ReloginServer;
                servers.Relogin(targetServer);
            });
            return IntPtr.Zero;
        }
        else if (uMsg == 0x0400 + 448) // WM_SKUA_LOGOUT
        {
            Ioc.Default.GetRequiredService<IScriptServers>().Logout();
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_JUMP_MAP)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_jump.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string[] lines = System.IO.File.ReadAllLines(tempFile);
                        string map = lines.Length > 0 ? lines[0].Trim() : "";
                        string cell = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]) ? lines[1].Trim() : "Enter";
                        string pad = "Spawn";
                        
                        if (cell.Contains(','))
                        {
                            var cellParts = cell.Split(',');
                            cell = cellParts[0].Trim();
                            if (cellParts.Length > 1) pad = cellParts[1].Trim();
                        }
                        
                        var mapService = Ioc.Default.GetRequiredService<IScriptMap>();
                        if (string.IsNullOrWhiteSpace(map) || mapService.Name.Equals(map.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                            mapService.Jump(cell, pad, false);
                        else
                            mapService.Join(map, cell, pad, true, false);
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_JUMP_PLAYER)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_jump_player.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string targetPlayer = System.IO.File.ReadAllText(tempFile)?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(targetPlayer))
                            Ioc.Default.GetRequiredService<IScriptPlayer>().Goto(targetPlayer);
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_LOAD_SCRIPT)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_script.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string targetScript = System.IO.File.ReadAllText(tempFile);
                        if (!string.IsNullOrWhiteSpace(targetScript) && System.IO.File.Exists(targetScript))
                        {
                            var sm = Ioc.Default.GetRequiredService<Skua.App.Avalonia.ViewModels.ScriptLoaderViewModel>();
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (sm.LoadScriptCommand.CanExecute(targetScript))
                                    sm.LoadScriptCommand.Execute(targetScript);
                            });
                        }
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_SET_OPTION)
        {
            int optionId = wParam.ToInt32();
            bool value = lParam.ToInt32() == 1;
            var options = Ioc.Default.GetRequiredService<IScriptOption>();
            switch (optionId)
            {
                case 1: options.LagKiller = value; break;
                case 2: options.HeadlessMode = value; break;
                case 3: options.HidePlayers = value; break;
                case 4: options.DisableFX = value; break;
                case 5: options.InfiniteRange = value; break;
                case 6: options.Magnetise = value; break;
                case 7: options.SkipCutscenes = value; break;
                case 8: options.UseFunctionBasedSkills = value; break;
                case 9: options.StreamerMode = value; break;
                case 99:
                    var sm = Ioc.Default.GetRequiredService<Skua.App.Avalonia.ViewModels.ScriptLoaderViewModel>();
                    var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
                    if (value)
                    {
                        if (!sm.ScriptManager.ScriptRunning)
                        {
                            if (sm.ToggleScriptCommand.CanExecute(null))
                                sm.ToggleScriptCommand.Execute(null);
                        }
                    }
                    else
                    {
                        if (scheduler.IsRunningQueue && scheduler.StopQueueCommand.CanExecute(null))
                            scheduler.StopQueueCommand.Execute(null);
                        else if (sm.ScriptManager.ScriptRunning && sm.ToggleScriptCommand.CanExecute(null))
                            sm.ToggleScriptCommand.Execute(null);
                    }
                    break;
            }
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_ARMY_SCHEDULER)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_playlist.json");
                    if (System.IO.File.Exists(tempFile))
                    {
                        var data = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel.SavedScriptItem>>(System.IO.File.ReadAllText(tempFile));
                        var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
                        Dispatcher.UIThread.Post(() =>
                        {
                            scheduler.ScriptQueue.Clear();
                            if (data != null)
                            {
                                foreach (var item in data)
                                {
                                    if (System.IO.File.Exists(item.Path))
                                    {
                                        var vm = new Skua.Shared.Avalonia.ViewModels.ScriptItemViewModel(item.Path) { Id = item.Id };
                                        if (!string.IsNullOrEmpty(item.Name)) vm.Name = item.Name;
                                        scheduler.ScriptQueue.Add(vm);
                                    }
                                }
                            }
                            if (scheduler.ScriptQueue.Count > 0 && !scheduler.IsRunningQueue)
                            {
                                if (scheduler.StartQueueCommand.CanExecute(null))
                                    scheduler.StartQueueCommand.Execute(null);
                            }
                        });
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_ARMY_SCHEDULER_STOP)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
                if (scheduler.StopQueueCommand.CanExecute(null))
                    scheduler.StopQueueCommand.Execute(null);
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_CHECK_LOGIN)
        {
            var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
            return new IntPtr(bot.Player.LoggedIn ? 1 : 0);
        }
        else if (uMsg == 0x0400 + 458) // WM_SKUA_ACCEPT_QUEST
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_accept_quest.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string[] lines = System.IO.File.ReadAllLines(tempFile);
                        string questIdRaw = lines.Length > 0 ? lines[0].Trim() : "";
                        string rewardIdRaw = lines.Length > 1 ? lines[1].Trim() : "-1";
                        int rewardId = int.TryParse(rewardIdRaw, out int rId) ? rId : -1;
                        if (int.TryParse(questIdRaw, out int questId) && questId > 0)
                        {
                            var quest = Ioc.Default.GetRequiredService<IScriptQuest>();
                            quest.RegisterQuests((questId, rewardId));
                        }
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_AUTO_ATTACK)
        {
            bool start = wParam.ToInt32() == 1;
            Dispatcher.UIThread.Post(() =>
            {
                var autoVm = Ioc.Default.GetRequiredService<AutoViewModel>();
                if (start)
                    StrongReferenceMessenger.Default.Send<StartAutoAttackMessage>();
                else
                    StrongReferenceMessenger.Default.Send<StopAutoMessage>();
            });
            return IntPtr.Zero;
        }
        else if (uMsg == 0x0400 + 452) // WM_SKUA_THROTTLE
        {
            bool throttle = wParam.ToInt32() == 1;
            var options = Ioc.Default.GetRequiredService<IScriptOption>();
            if (!options.HeadlessMode)
            {
                var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                try
                {
                    if (throttle)
                    {
                        if (_preThrottleFPS <= 0)
                        {
                            int currentFPS = bot.Flash?.GetGameObject<int>("stage.frameRate") ?? -1;
                            if (currentFPS > 2)
                                _preThrottleFPS = currentFPS;
                            string? currentQuality = bot.Flash?.GetGameObject<string>("stage.quality");
                            if (!string.IsNullOrEmpty(currentQuality))
                                _preThrottleQuality = currentQuality;
                        }

                        bot.Flash?.SetGameObject("stage.frameRate", 2);
                        bot.Flash?.SetGameObject("stage.quality", "LOW");
                        Skua.Core.Utils.MemoryUtils.TrimWorkingSet();
                    }
                    else
                    {
                        int currentFPS = bot.Flash?.GetGameObject<int>("stage.frameRate") ?? -1;
                        int targetFPS = _preThrottleFPS > 2 ? _preThrottleFPS : (currentFPS > 2 ? currentFPS : options.SetFPS);
                        string? targetQuality = _preThrottleQuality;
                        
                        _preThrottleFPS = -1;
                        _preThrottleQuality = null;

                        if (targetFPS > 0)
                            bot.Flash?.SetGameObject("stage.frameRate", targetFPS);
                        if (!string.IsNullOrEmpty(targetQuality))
                            bot.Flash?.SetGameObject("stage.quality", targetQuality);
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_OPEN_BANK)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Ioc.Default.GetRequiredService<IScriptBank>().Open();
                }
                catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == HotKeys.WM_SKUA_EXECUTE_HOTKEY)
        {
            int actionId = wParam.ToInt32();
            string? actionName = HotKeys.GetActionNameById(actionId);
            if (!string.IsNullOrEmpty(actionName))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (actionName == "OpenBank")
                            Ioc.Default.GetRequiredService<IScriptBank>().Open();
                        else
                            HotKeys.ExecuteHotkeyActionDirect(actionName);
                    }
                    catch { }
                });
            }
            return IntPtr.Zero;
        }
        else if (uMsg == HotKeys.WM_SKUA_RELOAD_HOTKEYS)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Ioc.Default.GetRequiredService<IHotKeyService>().Reload();
                }
                catch { }
            });
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

#if IS_WINDOWS
    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_NCACTIVATE)
        {
            if (wParam == IntPtr.Zero)
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fg, out uint fgPid);
                    if (fgPid == (uint)Environment.ProcessId)
                    {
                        return DefSubclassProc(hWnd, uMsg, (IntPtr)1, lParam);
                    }
                }
            }
        }
        else if (uMsg == 0x004A /* WM_COPYDATA */)
        {
            try
            {
                if (lParam != IntPtr.Zero)
                {
                    var cds = Marshal.PtrToStructure<HotKeys.COPYDATASTRUCT>(lParam);
                    if (cds.dwData == (IntPtr)0x484B && cds.lpData != IntPtr.Zero)
                    {
                        string? actionName = Marshal.PtrToStringUni(cds.lpData);
                        if (!string.IsNullOrEmpty(actionName))
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                try
                                {
                                    if (actionName == "OpenBank")
                                        Ioc.Default.GetRequiredService<IScriptBank>().Open();
                                    else
                                        HotKeys.ExecuteHotkeyActionDirect(actionName);
                                }
                                catch { }
                            });
                            return (IntPtr)1;
                        }
                    }
                }
            }
            catch { }
        }

        IntPtr res = HandleIpcMessage(uMsg, wParam, lParam);
        if (res != IntPtr.Zero)
            return res;

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
#endif
}

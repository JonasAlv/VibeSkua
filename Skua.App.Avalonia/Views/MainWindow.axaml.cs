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

namespace Skua.App.Avalonia.Views;

public partial class MainWindow : Window
{
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
        Skua.Shared.Avalonia.Controls.Shell.WindowTitleBar.SetArtworkPath(Ioc.Default.GetRequiredService<ISettingsService>().GetShared().TitleBarArtworkPath);
        MainMenuBar.DataContext = _mainMenuVm;
        ConfigureMainMenu();
        RegisterLifecycleHandlers();
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

    private void RegisterLifecycleHandlers()
    {
        Opened += MainWindow_Opened;
        Closing += (s, e) => App.DisposeTrayIcon();
        Closed += MainWindow_Closed;
        Deactivated += (s, e) =>
        {
            ReleaseAllPressedKeys();
            Skua.App.Avalonia.Controls.WinFormsFlashHost.ForwardFocus(false);
        };
        Activated += (s, e) =>
        {
            ReleaseAllPressedKeys();
            Skua.App.Avalonia.Controls.WinFormsFlashHost.ForwardFocus(true);
        };

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
        
        Focusable = true;
        InitializeLinuxInput();

        if (FlashHost != null)
        {
            FlashHost.IsVisible = !_options.HeadlessMode;
            Dispatcher.UIThread.RunJobs();
            FlashHost.Focus();
        }
        _flash.InitializeFlash();
        TabIpc.StartListener(this, Environment.ProcessId, _ipcCancelCts.Token);

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

                    // Enhance pressed on another tab with this account's profile selected
                    if (Skua.Core.Models.ConnectedAccounts.TakeEnhanceRequest())
                    {
                        var classEnhance = Ioc.Default.GetRequiredService<Skua.App.Avalonia.ViewModels.CoreBotsOptions.CBOClassEnhanceViewModel>();
                        classEnhance.SetValues(Skua.App.Avalonia.ViewModels.CoreBotsOptions.CoreBotsViewModel.ReadProfile(username) ?? new());
                        classEnhance.EnhanceCommand.Execute(null);
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
}

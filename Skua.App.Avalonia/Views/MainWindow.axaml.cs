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
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        if (_mainMenuVm.Plugins is INotifyCollectionChanged notifyPlugins)
        {
            notifyPlugins.CollectionChanged += (s, e) => Dispatcher.UIThread.Post(BuildMainMenu);
        }
    }

    private void MainMenuItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
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
            menuItem.Classes.Add("top-level");
            menuItem.Padding = new Avalonia.Thickness(12, 0);
            menuItem.Height = 36;
        }
        else
        {
            menuItem.Padding = new Avalonia.Thickness(12, 6);
        }

        if (viewModel is MainMenuItemViewModel item)
        {
            var headerContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (item.Icon != MaterialIconKind.None)
            {
                headerContent.Children.Add(new MaterialIcon
                {
                    Kind = item.Icon,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = isTopLevel ? Brushes.White : new SolidColorBrush(Color.Parse("#38BDF8"))
                });
            }

            headerContent.Children.Add(new TextBlock
            {
                Text = item.Header,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = isTopLevel ? FontWeight.SemiBold : FontWeight.Normal,
                FontSize = isTopLevel ? 12 : 13
            });

            menuItem.Header = headerContent;

            if (item.Header == "Plugins")
                _pluginsMenuItem = menuItem;

            foreach (var subItem in item.Items)
                menuItem.Items.Add(CreateMainMenuItem(subItem));
        }

        return menuItem;
    }

    private void RegisterLifecycleHandlers()
    {
        Opened += MainWindow_Opened;
        Closing += (s, e) => App.DisposeTrayIcon();
        Closed += MainWindow_Closed;

#if IS_LINUX
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
#endif

        if (_options is INotifyPropertyChanged notifyOptions)
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

#if IS_LINUX
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
                        string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                        Directory.CreateDirectory(dir);
                        string file = Path.Combine(dir, $"{Environment.ProcessId}.txt");
                        File.WriteAllText(file, username);
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
        try
        {
            string file = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs", $"{Environment.ProcessId}.txt");
            if (File.Exists(file))
                File.Delete(file);
        }
        catch { }

        _ipcCancelCts.Cancel();
        _flash.FlashCall -= OnFlashCall;
        _startup.Dispose();
        _flash.Dispose();
        StrongReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

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
                try
                {
                    FlashHost?.SetScaleMode(false);
                    _flash.Call("setGameObject", "stage.scaleMode", "showAll");
                }
                catch { }
            });
        }
    }
}

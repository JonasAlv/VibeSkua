using Avalonia.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using Skua.Manager.Avalonia.ViewModels;
using System;
using System.Runtime.InteropServices;

namespace Skua.Manager.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly ManagerMainViewModel _viewModel;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = Ioc.Default.GetRequiredService<ManagerMainViewModel>();
        DataContext = _viewModel;

        StrongReferenceMessenger.Default.Register<MainWindow, ShowMainWindowMessage>(this, ShowManager);
        StrongReferenceMessenger.Default.Register<MainWindow, ExitManagerMessage>(this, AllowClose);

        Opened += (s, e) =>
        {
            try
            {
                Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this);
            }
            catch { }
        };

        Activated += (s, e) =>
        {
            try
            {
                var accVm = Ioc.Default.GetService<AccountManagerViewModel>();
                accVm?.ScheduleReload();
            }
            catch { }
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            _viewModel.IsActive = false;
            return;
        }

        _viewModel.IsActive = false;
        StrongReferenceMessenger.Default.UnregisterAll(this);
        base.OnClosing(e);
    }

    private void ShowManager(MainWindow recipient, ShowMainWindowMessage message)
    {
        if (!recipient.IsVisible)
        {
            recipient.Show();
        }

        if (recipient.WindowState == WindowState.Minimized)
            recipient.WindowState = WindowState.Normal;

        recipient.Activate();
        recipient.Topmost = true;
        recipient.Topmost = false;
        recipient.Focus();
    }

    private void AllowClose(MainWindow recipient, ExitManagerMessage message)
    {
        recipient._allowClose = true;
    }
}

using Avalonia.Controls;
using Skua.App.Avalonia.ViewModels.CoreBotsOptions;
using System;

namespace Skua.App.Avalonia.Views;

public partial class CoreBotsView : UserControl
{
    public CoreBotsView() => InitializeComponent();

    private void ProfileComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is CoreBotsViewModel vm)
            vm.RefreshProfiles();
    }
}

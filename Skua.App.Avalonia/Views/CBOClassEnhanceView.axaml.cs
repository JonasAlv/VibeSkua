using Avalonia.Controls;
using Skua.App.Avalonia.ViewModels.CoreBotsOptions;

namespace Skua.App.Avalonia.Views;

public partial class CBOClassEnhanceView : UserControl
{
    public CBOClassEnhanceView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is CBOClassEnhanceViewModel vm)
                vm.RefreshInventoryCommand.Execute(null);
        };
    }
}

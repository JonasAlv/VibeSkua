using Skua.App.Avalonia.ViewModels.CoreBotsOptions;
using Skua.Core.Interfaces;

namespace Skua.App.Avalonia.Services;

public class ClassEnhanceProfiles : IClassEnhanceProfiles
{
    public ClassEnhanceProfiles(IWindowService windowService, CoreBotsViewModel coreBots)
    {
        _windowService = windowService;
        _coreBots = coreBots;
    }

    private readonly IWindowService _windowService;
    private readonly CoreBotsViewModel _coreBots;

    public string Resolve(string account, string className) =>
        CBOClassEnhanceViewModel.Resolve(CoreBotsViewModel.ReadProfile(account), className);

    public void Edit(string account, string className)
    {
        _windowService.ShowManagedWindow("CoreBots");
        _coreBots.EditClassEnhance(account, className);
    }
}

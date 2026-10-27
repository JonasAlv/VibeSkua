using Skua.Core.Scripts;
using Skua.Shared.Avalonia.ViewModels;

namespace Skua.App.Avalonia.ViewModels.Options;

public sealed class WatchDogViewModel(ScriptOption options) : BotControlViewModelBase("Watch Dog", 620, 460)
{
    public ScriptOption Options { get; } = options;
}

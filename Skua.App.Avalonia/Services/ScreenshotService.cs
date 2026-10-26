using System;
using System.Threading.Tasks;
using Skua.Core.Interfaces;

namespace Skua.App.Avalonia.Services;

public class ScreenshotService : IScreenshotService
{
    public Task<byte[]> TakeScreenshotAsync()
    {
        return Task.FromResult(Array.Empty<byte>());
    }
}

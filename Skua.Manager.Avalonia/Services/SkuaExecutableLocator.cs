using System.IO;
using System;

namespace Skua.Manager.Avalonia.Services;

internal static class SkuaExecutableLocator
{
    public static string ExecutablePath { get; } = Resolve();

    public static string WorkingDirectory =>
        Path.GetDirectoryName(ExecutablePath) ?? AppContext.BaseDirectory;

    private static string Resolve()
    {
        string[] directCandidates = OperatingSystem.IsWindows()
            ? [ "VibeSkua.exe", "Skua.exe" ]
            : [ "VibeSkua", "Skua", "VibeSkua.exe", "Skua.exe" ];

        foreach (string name in directCandidates)
        {
            string p = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(p))
                return p;
        }

        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string[] candidates =
        [
            Path.Combine(repositoryRoot, "Build", "VibeSkua"),
            Path.Combine(repositoryRoot, "Build", "VibeSkua.exe"),
            Path.Combine(repositoryRoot, "Build", "Skua"),
            Path.Combine(repositoryRoot, "Build", "Skua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0", "VibeSkua"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0", "VibeSkua.dll"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0-windows", "VibeSkua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0-windows", "Skua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Debug", "net10.0", "VibeSkua"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Debug", "net10.0-windows", "VibeSkua.exe")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        string fallbackName = OperatingSystem.IsWindows() ? "VibeSkua.exe" : "VibeSkua";
        return Path.Combine(AppContext.BaseDirectory, fallbackName);
    }
}

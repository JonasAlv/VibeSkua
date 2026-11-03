using System;
using System.IO;

namespace Skua.Manager.Avalonia.Services;

internal static class SkuaExecutableLocator
{
    public static string ExecutablePath { get; } = Resolve();

    public static string WorkingDirectory =>
        Path.GetDirectoryName(ExecutablePath) ?? AppContext.BaseDirectory;

    private static string Resolve()
    {
        // If running inside an AppImage, check if a VibeSkua AppImage exists next to this manager AppImage
        string? appImageEnv = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrEmpty(appImageEnv))
        {
            string? hostDir = Path.GetDirectoryName(appImageEnv);
            if (!string.IsNullOrEmpty(hostDir))
            {
                string[] appImageCandidates = [ "VibeSkua-x86_64.AppImage", "VibeSkua.AppImage", "VibeSkua" ];
                foreach (string name in appImageCandidates)
                {
                    string p = Path.Combine(hostDir, name);
                    if (File.Exists(p) && !string.Equals(p, appImageEnv, StringComparison.Ordinal))
                        return p;
                }
            }
        }

        string[] directCandidates = [ "VibeSkua-x86_64.AppImage", "VibeSkua.AppImage", "VibeSkua", "Skua" ];

        foreach (string name in directCandidates)
        {
            string p = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(p))
                return p;
        }

        // Check standard Linux user directories where AppImages might be placed
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] searchDirs =
        [
            Path.Combine(userHome, "Games", "VibeSkua-linux-AppImage"),
            Path.Combine(userHome, "Applications"),
            Path.Combine(userHome, "Desktop"),
            Path.Combine(userHome, "Downloads"),
            Path.Combine(userHome, ".local", "bin"),
            Path.Combine(userHome, ".local", "share", "VibeSkua"),
            "/opt/VibeSkua"
        ];

        foreach (string dir in searchDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (string name in directCandidates)
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p))
                    return p;
            }
        }

        // Check system PATH
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string name in directCandidates)
                {
                    string candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        // Check repository build paths
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string[] candidates =
        [
            Path.Combine(repositoryRoot, "Build", "VibeSkua-x86_64.AppImage"),
            Path.Combine(repositoryRoot, "Build", "VibeSkua"),
            Path.Combine(repositoryRoot, "Build", "Skua"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0", "VibeSkua"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Debug", "net10.0", "VibeSkua")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(AppContext.BaseDirectory, "VibeSkua");
    }
}

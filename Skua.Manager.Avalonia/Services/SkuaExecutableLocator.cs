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
                    if (File.Exists(p) && !string.Equals(p, appImageEnv, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }
        }

#if IS_LINUX
        string[] directCandidates = [ "VibeSkua-x86_64.AppImage", "VibeSkua.AppImage", "VibeSkua", "Skua" ];
#else
        string[] directCandidates = OperatingSystem.IsWindows()
            ? [ "VibeSkua.exe", "Skua.exe" ]
            : [ "VibeSkua", "Skua", "VibeSkua-x86_64.AppImage", "VibeSkua.AppImage", "VibeSkua.exe", "Skua.exe" ];
#endif

        foreach (string name in directCandidates)
        {
            string p = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(p))
                return p;
        }

#if IS_LINUX
        // 2. Check standard Linux user directories where AppImages might be placed
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

        // 3. Check system PATH
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

        // 4. Check repository build paths
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string[] candidates =
        [
            Path.Combine(repositoryRoot, "Build", "VibeSkua-x86_64.AppImage"),
            Path.Combine(repositoryRoot, "Build", "VibeSkua"),
            Path.Combine(repositoryRoot, "Build", "Skua"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0", "VibeSkua"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Debug", "net10.0", "VibeSkua")
        ];
#else
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string[] candidates =
        [
            Path.Combine(repositoryRoot, "Build", "VibeSkua.exe"),
            Path.Combine(repositoryRoot, "Build", "Skua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0-windows", "VibeSkua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0-windows", "Skua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Debug", "net10.0-windows", "VibeSkua.exe")
        ];
#endif

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

#if IS_LINUX
        string fallbackName = "VibeSkua";
#else
        string fallbackName = OperatingSystem.IsWindows() ? "VibeSkua.exe" : "VibeSkua";
#endif
        return Path.Combine(AppContext.BaseDirectory, fallbackName);
    }
}

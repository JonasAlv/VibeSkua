using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Velopack;

using Skua.Core.Models;

namespace Skua.App.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        InitializeAssemblyResolvers();
        RunApp(args);
    }

    private static void InitializeAssemblyResolvers()
    {
        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            if (assemblyName.Name == null || assemblyName.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                return null;

            string fileName = $"{assemblyName.Name}.dll";
            string rootPath = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(rootPath))
                return context.LoadFromAssemblyPath(rootPath);

            return null;
        };

        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            if (args.Name.Contains(".resources"))
                return null;

            Assembly? existing = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName == args.Name);
            if (existing != null)
                return existing;

            string assemblyName = new AssemblyName(args.Name).Name + ".dll";
            string rootPath = Path.Combine(AppContext.BaseDirectory, assemblyName);
            if (File.Exists(rootPath))
                return Assembly.LoadFrom(rootPath);

            return null;
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApp(string[] args)
    {
        try
        {
            ClientFileSources.EnsureInitialized();
            System.Threading.Tasks.Task.Run(() => Skua.App.Avalonia.Flash.FlashUtil.PreloadSwf());
            VelopackApp.Build().Run();
            if (Skua.Shared.Avalonia.Utils.AppShortcutHelper.IsInstalled())
            {
                Skua.Shared.Avalonia.Utils.AppShortcutHelper.CreateOrUpdateShortcuts(createIfMissing: false);
            }
            Environment.CurrentDirectory = AppContext.BaseDirectory;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash_program.txt"), ex.ToString());
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();
#if DEBUG
        builder = builder.WithDeveloperTools();
#endif
        return builder.LogToTrace();
    }
}

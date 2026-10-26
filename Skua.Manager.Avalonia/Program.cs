using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using Skua.Shared.Avalonia.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using Velopack;

namespace Skua.Manager.Avalonia;

internal static class Program
{
    private static EventWaitHandle? _singleInstanceWaitHandle;
    private static Mutex? _singleInstanceMutex;

    static Program()
    {
        InitializeAssemblyResolvers();
    }

    [STAThread]
    public static void Main(string[] args)
    {
        InitializeAssemblyResolvers();

        VelopackApp.Build().Run();

        if (OperatingSystem.IsWindows())
        {
            const string eventName = @"Global\VibeSkua.Manager.SingleInstance.WakeEvent";
            bool createdNew;
            try
            {
                _singleInstanceWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, eventName, out createdNew);
            }
            catch
            {
                _singleInstanceWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, "VibeSkua.Manager.SingleInstance.WakeEvent", out createdNew);
            }

            if (!createdNew)
            {
                // Another manager is already running! Wake it up and terminate this second instance immediately.
                try
                {
                    _singleInstanceWaitHandle.Set();
                }
                catch
                {
                }
                return;
            }

            // We are the single running instance. Listen for signals from subsequent launch attempts.
            ThreadPool.RegisterWaitForSingleObject(_singleInstanceWaitHandle, (state, timedOut) =>
            {
                if (!timedOut)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        StrongReferenceMessenger.Default.Send(new ShowMainWindowMessage());
                    });
                }
            }, null, Timeout.Infinite, false);
        }
        else
        {
            bool createdNew;
            try
            {
                _singleInstanceMutex = new Mutex(true, "VibeSkuaManagerSingleInstanceMutex", out createdNew);
            }
            catch
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                return;
            }
        }

        RunApp(args);
    }

    private static void InitializeAssemblyResolvers()
    {
        string assembliesDir = Path.Combine(AppContext.BaseDirectory, "Assemblies");
        HashSet<string>? assembliesSet = null;
        if (Directory.Exists(assembliesDir))
        {
            try
            {
                assembliesSet = new HashSet<string>(
                    Directory.EnumerateFiles(assembliesDir, "*.dll").Select(Path.GetFileName)!,
                    StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
            }
        }

        string arch = OperatingSystem.IsWindows()
            ? (Environment.Is64BitProcess ? "win-x64" : "win-x86")
            : (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64");

        string runtimeNativeDir = Path.Combine(AppContext.BaseDirectory, "Assemblies", "runtimes", arch, "native");
        HashSet<string>? runtimeNativeSet = null;
        if (Directory.Exists(runtimeNativeDir))
        {
            try
            {
                runtimeNativeSet = new HashSet<string>(
                    Directory.EnumerateFiles(runtimeNativeDir).Select(Path.GetFileName)!,
                    StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
            }
        }

        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            if (assemblyName.Name == null || assemblyName.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                return null;

            string fileName = $"{assemblyName.Name}.dll";
            if (assembliesSet != null && assembliesSet.Contains(fileName))
            {
                return context.LoadFromAssemblyPath(Path.Combine(assembliesDir, fileName));
            }

            string rootPath = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(rootPath))
                return context.LoadFromAssemblyPath(rootPath);

            return null;
        };

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (assembly, unmanagedDllName) =>
        {
            string ext = OperatingSystem.IsWindows() ? ".dll" : ".so";
            string fileName = unmanagedDllName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                ? unmanagedDllName
                : $"{unmanagedDllName}{ext}";

            if (assembliesSet != null && assembliesSet.Contains(fileName))
                return NativeLibrary.Load(Path.Combine(assembliesDir, fileName));

            if (runtimeNativeSet != null && runtimeNativeSet.Contains(fileName))
                return NativeLibrary.Load(Path.Combine(runtimeNativeDir, fileName));

            string rootNative = Path.Combine(AppContext.BaseDirectory, "runtimes", arch, "native", fileName);
            if (File.Exists(rootNative))
                return NativeLibrary.Load(rootNative);

            return IntPtr.Zero;
        };

        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            if (args.Name.Contains(".resources"))
                return null;

            Assembly? existing = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName == args.Name);
            if (existing != null)
                return existing;

            string assemblyName = new AssemblyName(args.Name).Name + ".dll";
            if (assembliesSet != null && assembliesSet.Contains(assemblyName))
            {
                return Assembly.LoadFrom(Path.Combine(assembliesDir, assemblyName));
            }

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
            Skua.Core.Models.ClientFileSources.EnsureInitialized();
            if (AppShortcutHelper.IsInstalled())
            {
                AppShortcutHelper.CreateOrUpdateShortcuts(createIfMissing: false);
            }
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash_manager.txt"), ex.ToString());
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();

        if (OperatingSystem.IsWindows())
        {
            builder = builder.With(new Win32PlatformOptions
            {
                RenderingMode = new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Software },
                CompositionMode = new[] { Win32CompositionMode.WinUIComposition, Win32CompositionMode.RedirectionSurface }
            });
        }
#if DEBUG
        builder = builder.WithDeveloperTools();
#endif
        return builder.LogToTrace();
    }
}

using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using Skua.Shared.Avalonia.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Velopack;

namespace Skua.Manager.Avalonia;

internal static class Program
{
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

#if IS_LINUX
        string socketPath = Path.Combine(Path.GetTempPath(), $"vibeskua-manager-{Environment.UserName}.sock");

        // Attempt to signal existing primary instance
        try
        {
            using Socket client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(socketPath));
            client.Send(Encoding.UTF8.GetBytes("SHOW\n"));
            client.Close();
            // Existing instance alerted, exit cleanly
            return;
        }
        catch
        {
            // Socket doesn't exist or is stale
            try { if (File.Exists(socketPath)) File.Delete(socketPath); } catch { }
        }

        try
        {
            Socket listenerSocket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listenerSocket.Bind(new UnixDomainSocketEndPoint(socketPath));
            listenerSocket.Listen(5);

            Task.Run(async () =>
            {
                byte[] buffer = new byte[64];
                while (true)
                {
                    try
                    {
                        using Socket handler = await listenerSocket.AcceptAsync();
                        int read = await handler.ReceiveAsync(buffer, SocketFlags.None);
                        if (read > 0)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                StrongReferenceMessenger.Default.Send(new ShowMainWindowMessage());
                            });
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            });

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try { listenerSocket.Dispose(); } catch { }
                try { if (File.Exists(socketPath)) File.Delete(socketPath); } catch { }
            };
        }
        catch
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "VibeSkuaManagerSingleInstanceMutex", out createdNew);
            if (!createdNew)
                return;
        }
#else
        bool createdNewFallback;
        _singleInstanceMutex = new Mutex(true, "VibeSkuaManagerSingleInstanceMutex", out createdNewFallback);
        if (!createdNewFallback)
            return;
#endif

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

        string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";

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
            string ext = ".so";
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

#if DEBUG
        builder = builder.WithDeveloperTools();
#endif
        return builder.LogToTrace();
    }
}

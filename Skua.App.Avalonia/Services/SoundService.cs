using Skua.Core.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Services;

public class SoundService : ISoundService
{
#if IS_LINUX
    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XBell(IntPtr display, int percent);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    private static readonly string[] SoundCandidates =
    [
        "/usr/share/sounds/freedesktop/stereo/bell.oga",
        "/usr/share/sounds/freedesktop/stereo/complete.oga",
        "/usr/share/sounds/freedesktop/stereo/message.oga"
    ];

    private static void PlayLinuxSound()
    {
        Task.Run(() =>
        {
            try
            {
                // 1. Try canberra-gtk-play
                try
                {
                    using var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "canberra-gtk-play",
                        Arguments = "-i bell",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (p != null) return;
                }
                catch { }

                // 2. Try paplay with standard freedesktop sound
                string? soundPath = null;
                foreach (string candidate in SoundCandidates)
                {
                    if (File.Exists(candidate))
                    {
                        soundPath = candidate;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(soundPath))
                {
                    try
                    {
                        using var p = Process.Start(new ProcessStartInfo
                        {
                            FileName = "paplay",
                            Arguments = $"\"{soundPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        if (p != null) return;
                    }
                    catch { }
                }

                // 3. Fallback to X11 display bell
                IntPtr disp = XOpenDisplay(IntPtr.Zero);
                if (disp != IntPtr.Zero)
                {
                    XBell(disp, 50);
                    XFlush(disp);
                    XCloseDisplay(disp);
                    return;
                }
            }
            catch { }

            // 4. Fallback to Console.Beep
            try { Console.Beep(); } catch { }
        });
    }
#endif

    public void Beep()
    {
#if IS_LINUX
        PlayLinuxSound();
#else
        try
        {
            Console.Beep();
        }
        catch { }
#endif
    }

    public void Beep(int frequency, int duration)
    {
#if IS_LINUX
        PlayLinuxSound();
#else
        try
        {
            Console.Beep(frequency, duration);
        }
        catch { }
#endif
    }
}

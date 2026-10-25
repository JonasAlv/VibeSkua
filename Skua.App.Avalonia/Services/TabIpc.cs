using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Skua.App.Avalonia.Views;

namespace Skua.App.Avalonia.Services;

/// <summary>
/// Cross-platform lightweight IPC between TabbedHostWindow (host) and MainWindow (child instances)
/// using asynchronous named pipes.
/// </summary>
public static class TabIpc
{
    public static void SendMessage(int childPid, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (childPid <= 0)
            return;

        Task.Run(async () =>
        {
            try
            {
                using var client = new NamedPipeClientStream(".", $"VibeSkuaChildPipe_{childPid}", PipeDirection.Out);
                await client.ConnectAsync(400);
                using var writer = new StreamWriter(client);
                await writer.WriteLineAsync($"{msg}:{wParam.ToInt64()}:{lParam.ToInt64()}");
                await writer.FlushAsync();
            }
            catch { }
        });
    }

    public static void StartListener(MainWindow window, int pid, CancellationToken ct)
    {
        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        $"VibeSkuaChildPipe_{pid}",
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    using (ct.Register(() => { try { server?.Dispose(); } catch { } }))
                    {
                        await server.WaitForConnectionAsync(ct);
                    }

                    using (server)
                    using (var reader = new StreamReader(server))
                    {
                        string? line = await reader.ReadLineAsync();
                        if (!string.IsNullOrEmpty(line))
                        {
                            var parts = line.Split(':');
                            if (parts.Length >= 3 &&
                                uint.TryParse(parts[0], out uint msg) &&
                                long.TryParse(parts[1], out long wParam) &&
                                long.TryParse(parts[2], out long lParam))
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    window.HandleIpcMessage(msg, (IntPtr)wParam, (IntPtr)lParam);
                                });
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    try { server?.Dispose(); } catch { }
                    break;
                }
                catch
                {
                    try { server?.Dispose(); } catch { }
                    if (ct.IsCancellationRequested) break;
                    await Task.Delay(50, ct);
                }
            }
        }, ct);
    }
}

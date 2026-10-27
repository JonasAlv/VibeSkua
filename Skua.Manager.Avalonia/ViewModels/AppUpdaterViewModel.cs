using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Manager.Avalonia.ViewModels;

public partial class AppUpdaterViewModel : ObservableObject
{
    private readonly IDispatcherService _dispatcherService;
    private const string RepoOwner = "JonasAlv";
    private const string RepoName = "VibeSkua";
    private const string TarAssetName = "VibeSkua-linux-AppImage.tar.gz";

    public AppUpdaterViewModel(IDispatcherService dispatcherService)
    {
        _dispatcherService = dispatcherService;
    }

    [ObservableProperty]
    private string _updateStatus = "Ready to check for updates.";

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private int _progressValue;

    private string? _remoteDownloadUrl;
    private string? _remoteTagName;
    private long _assetSize;

    public async Task CheckForUpdateAsync()
    {
        _dispatcherService.Invoke(() =>
        {
            IsChecking = true;
            UpdateStatus = "Checking for updates...";
            UpdateAvailable = false;
            ProgressValue = 0;
        });

        _remoteDownloadUrl = null;
        _remoteTagName = null;
        _assetSize = 0;

        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VibeSkua-Manager-Updater");
            client.Timeout = TimeSpan.FromSeconds(15);

            string apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using HttpResponseMessage response = await client.GetAsync(apiUrl);

            if (!response.IsSuccessStatusCode)
            {
                _dispatcherService.Invoke(() =>
                {
                    UpdateStatus = response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "No releases found on GitHub."
                        : $"GitHub check failed: {response.ReasonPhrase}";
                    UpdateAvailable = false;
                });
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out JsonElement tagEl)
                ? tagEl.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrEmpty(tagName))
            {
                _dispatcherService.Invoke(() =>
                {
                    UpdateStatus = "No valid release tag found.";
                    UpdateAvailable = false;
                });
                return;
            }

            // Look for VibeSkua-linux-AppImage.tar.gz
            if (root.TryGetProperty("assets", out JsonElement assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assetsEl.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out JsonElement nameEl)
                        ? nameEl.GetString() ?? string.Empty
                        : string.Empty;

                    if (name.Equals(TarAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        _remoteDownloadUrl = asset.TryGetProperty("browser_download_url", out JsonElement urlEl)
                            ? urlEl.GetString()
                            : null;
                        _assetSize = asset.TryGetProperty("size", out JsonElement sizeEl)
                            ? sizeEl.GetInt64()
                            : 0;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(_remoteDownloadUrl))
            {
                _dispatcherService.Invoke(() =>
                {
                    UpdateStatus = $"Release {tagName} found, but {TarAssetName} is missing.";
                    UpdateAvailable = false;
                });
                return;
            }

            _remoteTagName = tagName;

            // Compare version
            string remoteVerClean = tagName.TrimStart('v', 'V');
            string localVerClean = ClientFileSources.DisplayVersion.TrimStart('v', 'V');

            bool isNewer = false;
            string remoteVerCore = remoteVerClean.Split('-')[0];
            string localVerCore = localVerClean.Split('-')[0];

            if (Version.TryParse(remoteVerCore, out Version? remoteV) && Version.TryParse(localVerCore, out Version? localV))
            {
                isNewer = remoteV > localV;
            }
            else
            {
                isNewer = !string.Equals(remoteVerClean, localVerClean, StringComparison.OrdinalIgnoreCase);
            }

            _dispatcherService.Invoke(() =>
            {
                if (isNewer)
                {
                    UpdateStatus = $"Update {tagName} available! (Current: V{ClientFileSources.DisplayVersion})";
                    UpdateAvailable = true;
                }
                else
                {
                    UpdateStatus = $"You are up to date! (V{ClientFileSources.DisplayVersion})";
                    UpdateAvailable = false;
                }
            });
        }
        catch (Exception ex)
        {
            _dispatcherService.Invoke(() =>
            {
                UpdateStatus = $"Error checking updates: {ex.Message}";
                UpdateAvailable = false;
            });
        }
        finally
        {
            _dispatcherService.Invoke(() => IsChecking = false);
        }
    }

    [RelayCommand]
    private async Task CheckForUpdate()
    {
        await CheckForUpdateAsync();
    }

    public async Task DownloadAndInstallAsync()
    {
        if (string.IsNullOrEmpty(_remoteDownloadUrl))
            return;

        _dispatcherService.Invoke(() =>
        {
            IsChecking = true;
            UpdateStatus = "Starting download...";
            ProgressValue = 0;
        });

        string tempDir = Path.Combine(Path.GetTempPath(), "VibeSkuaUpdate_" + Guid.NewGuid().ToString("N")[..8]);
        string tarGzPath = Path.Combine(tempDir, TarAssetName);
        string extractDir = Path.Combine(tempDir, "extracted");

        try
        {
            Directory.CreateDirectory(tempDir);

            // Determine where the target AppImages reside
            string? runningAppImage = Environment.GetEnvironmentVariable("APPIMAGE");
            string targetDir = !string.IsNullOrEmpty(runningAppImage) && File.Exists(runningAppImage)
                ? Path.GetDirectoryName(runningAppImage)!
                : AppContext.BaseDirectory;

            // Download archive with progress
            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("VibeSkua-Manager-Updater");
                using HttpResponseMessage resp = await client.GetAsync(_remoteDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                long totalBytes = resp.Content.Headers.ContentLength ?? _assetSize;
                await using Stream contentStream = await resp.Content.ReadAsStreamAsync();
                await using FileStream fileStream = new(tarGzPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    totalRead += read;
                    if (totalBytes > 0)
                    {
                        int pct = (int)((totalRead * 100) / totalBytes);
                        _dispatcherService.Invoke(() =>
                        {
                            ProgressValue = pct;
                            UpdateStatus = $"Downloading {pct}% ({totalRead / (1024 * 1024)} MB / {totalBytes / (1024 * 1024)} MB)...";
                        });
                    }
                }
            }

            _dispatcherService.Invoke(() => UpdateStatus = "Extracting update archive...");
            Directory.CreateDirectory(extractDir);

            await Task.Run(() =>
            {
                using FileStream gzStream = File.OpenRead(tarGzPath);
                using GZipStream decompressedStream = new(gzStream, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(decompressedStream, extractDir, overwriteFiles: true);
            });

            _dispatcherService.Invoke(() => UpdateStatus = "Applying update...");

            string[] extractedAppImages = Directory.GetFiles(extractDir, "*.AppImage", SearchOption.AllDirectories);
            if (extractedAppImages.Length == 0)
            {
                throw new InvalidOperationException("No .AppImage binaries found inside the update package.");
            }

            foreach (string extracted in extractedAppImages)
            {
                string fileName = Path.GetFileName(extracted);
                string destination = Path.Combine(targetDir, fileName);

                // Set executable permissions (0755: rwxr-xr-x)
#if IS_LINUX
                if (OperatingSystem.IsLinux())
                {
                    try
                    {
                        File.SetUnixFileMode(extracted,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }
                    catch { }
                }
#endif

                // On Linux, directly overwriting or truncating a currently running binary/AppImage
                // throws ETXTBSY (Text file busy). To cleanly replace it, rename or unlink the existing
                // binary first so the running inode remains valid while the path points to the new binary.
                string backupOld = destination + ".old";
                if (File.Exists(destination))
                {
                    try
                    {
                        if (File.Exists(backupOld))
                            File.Delete(backupOld);
                        File.Move(destination, backupOld);
                    }
                    catch
                    {
                        try { File.Delete(destination); } catch { }
                    }
                }

                try
                {
                    File.Move(extracted, destination);
                }
                catch
                {
                    // Fallback for cross-filesystem move
                    File.Copy(extracted, destination, overwrite: true);
                    try { File.Delete(extracted); } catch { }
                }

                // Clean up backup if unlinked successfully
                try
                {
                    if (File.Exists(backupOld))
                        File.Delete(backupOld);
                }
                catch { }
            }

            // Cleanup temp files
            try { Directory.Delete(tempDir, true); } catch { }

            _dispatcherService.Invoke(() =>
            {
                UpdateStatus = "Update complete! Restarting VibeSkua Manager...";
                ProgressValue = 100;
            });

            await Task.Delay(1000);

            // Restart Manager
            string restartPath = !string.IsNullOrEmpty(runningAppImage) && File.Exists(runningAppImage)
                ? runningAppImage
                : (Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(targetDir, "VibeSkua.Manager"));

            if (File.Exists(restartPath))
            {
                Process.Start(new ProcessStartInfo(restartPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = targetDir
                });
            }

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }

            _dispatcherService.Invoke(() =>
            {
                UpdateStatus = $"Error updating: {ex.Message}";
                UpdateAvailable = true;
            });
        }
        finally
        {
            _dispatcherService.Invoke(() => IsChecking = false);
        }
    }

    [RelayCommand]
    private async Task DownloadAndInstall()
    {
        await DownloadAndInstallAsync();
    }
}

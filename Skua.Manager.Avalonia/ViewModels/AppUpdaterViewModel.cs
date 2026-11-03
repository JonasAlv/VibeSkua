using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
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
    private string? _remoteAssetDigest;
    private string? _remoteChecksumUrl;
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
        _remoteAssetDigest = null;
        _remoteChecksumUrl = null;
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
                        _remoteAssetDigest = asset.TryGetProperty("digest", out JsonElement digestEl)
                            ? digestEl.GetString()
                            : null;
                        _assetSize = asset.TryGetProperty("size", out JsonElement sizeEl)
                            ? sizeEl.GetInt64()
                            : 0;
                    }
                    else if (name.Equals(TarAssetName + ".sha256", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        _remoteChecksumUrl = asset.TryGetProperty("browser_download_url", out JsonElement csUrlEl)
                            ? csUrlEl.GetString()
                            : null;
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

        string updatesRoot = Path.Combine(ClientFileSources.SkuaDIR, ".updates");
        string tempDir = Path.Combine(updatesRoot, "update_" + Guid.NewGuid().ToString("N"));
        string tarGzPath = Path.Combine(tempDir, TarAssetName);
        string extractDir = Path.Combine(tempDir, "extracted");

        try
        {
            Directory.CreateDirectory(updatesRoot);
            ClientFileSources.RestrictDirectoryPermissions(updatesRoot);
            Directory.CreateDirectory(tempDir);
            ClientFileSources.RestrictDirectoryPermissions(tempDir);

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
                await using (FileStream fileStream = new(tarGzPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    ClientFileSources.RestrictFilePermissions(tarGzPath);

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

                string? expectedSha256 = null;
                if (!string.IsNullOrWhiteSpace(_remoteAssetDigest) &&
                    _remoteAssetDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    expectedSha256 = _remoteAssetDigest.Substring("sha256:".Length).Trim();
                }
                else if (!string.IsNullOrWhiteSpace(_remoteChecksumUrl))
                {
                    string checksumBody = await client.GetStringAsync(_remoteChecksumUrl);
                    foreach (string rawLine in checksumBody.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (rawLine.Contains(TarAssetName, StringComparison.OrdinalIgnoreCase) || !rawLine.Contains(' '))
                        {
                            string candidate = rawLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                            if (candidate.Length == 64)
                            {
                                expectedSha256 = candidate;
                                break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(expectedSha256))
                {
                    _dispatcherService.Invoke(() => UpdateStatus = "Verifying SHA-256 integrity...");
                    await using FileStream verifyStream = File.OpenRead(tarGzPath);
                    byte[] actualHash = await SHA256.HashDataAsync(verifyStream);
                    string actualHex = Convert.ToHexString(actualHash);
                    if (!actualHex.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"SHA-256 verification failed (expected {expectedSha256}, got {actualHex}).");
                    }
                }
            }

            _dispatcherService.Invoke(() => UpdateStatus = "Extracting update archive...");
            Directory.CreateDirectory(extractDir);
            ClientFileSources.RestrictDirectoryPermissions(extractDir);

            await Task.Run(() =>
            {
                string extractFullDir = Path.GetFullPath(extractDir);
                string extractPrefix = extractFullDir.EndsWith(Path.DirectorySeparatorChar)
                    ? extractFullDir
                    : extractFullDir + Path.DirectorySeparatorChar;

                using FileStream gzStream = File.OpenRead(tarGzPath);
                using GZipStream decompressedStream = new(gzStream, CompressionMode.Decompress);
                using TarReader reader = new(decompressedStream);

                while (reader.GetNextEntry() is { } entry)
                {
                    if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink or
                        TarEntryType.BlockDevice or TarEntryType.CharacterDevice or TarEntryType.Fifo)
                    {
                        throw new InvalidDataException($"Refusing unsafe tar entry '{entry.Name}' ({entry.EntryType}).");
                    }

                    if (string.IsNullOrWhiteSpace(entry.Name))
                        continue;

                    string destPath = Path.GetFullPath(Path.Combine(extractFullDir, entry.Name));
                    if (!destPath.StartsWith(extractPrefix, StringComparison.Ordinal) &&
                        !string.Equals(destPath, extractFullDir, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"Refusing path-traversal tar entry '{entry.Name}'.");
                    }

                    if (entry.EntryType == TarEntryType.Directory)
                    {
                        Directory.CreateDirectory(destPath);
                        ClientFileSources.RestrictDirectoryPermissions(destPath);
                    }
                    else if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                    {
                        string? parent = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            Directory.CreateDirectory(parent);
                            ClientFileSources.RestrictDirectoryPermissions(parent);
                        }
                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                }
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
                try
                {
                    File.SetUnixFileMode(extracted,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch { }

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

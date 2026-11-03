using Skua.Core.Interfaces;
using Skua.Core.Models;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Skua.Core.Services;

public class ClientUpdateService : IClientUpdateService
{
    private const string RepoOwner = "JonasAlv";
    private const string RepoName = "VibeSkua";
    private const string TarAssetName = "VibeSkua-linux-AppImage.tar.gz";

    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;

    private string? _remoteTagName;
    private string? _remoteDownloadUrl;
    private string? _remoteAssetDigest;
    private string? _remoteChecksumUrl;
    private long _assetSize;

    public ClientUpdateService(IDialogService dialogService, ISettingsService settingsService)
    {
        _dialogService = dialogService;
        _settingsService = settingsService;
    }

    public string? LatestVersion => _remoteTagName;

    public bool UpdateAvailable { get; private set; }

    public bool UsingNightlyChannel => _settingsService.Get("UseNightlyBuilds", false);

    public async Task CheckForUpdateAsync()
    {
        UpdateAvailable = false;
        _remoteTagName = null;
        _remoteDownloadUrl = null;
        _remoteAssetDigest = null;
        _remoteChecksumUrl = null;
        _assetSize = 0;

        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VibeSkua-ClientUpdateService");

            string apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using HttpResponseMessage response = await client.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode)
                return;

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out JsonElement tagEl)
                ? tagEl.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrEmpty(tagName))
                return;

            if (root.TryGetProperty("assets", out JsonElement assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assetsEl.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out JsonElement nameEl)
                        ? nameEl.GetString() ?? string.Empty
                        : string.Empty;

                    if (name.Equals(TarAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        _remoteDownloadUrl = asset.TryGetProperty("browser_download_url", out JsonElement urlEl) ? urlEl.GetString() : null;
                        _remoteAssetDigest = asset.TryGetProperty("digest", out JsonElement digestEl) ? digestEl.GetString() : null;
                        _assetSize = asset.TryGetProperty("size", out JsonElement sizeEl) ? sizeEl.GetInt64() : 0;
                    }
                    else if (name.Equals(TarAssetName + ".sha256", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        _remoteChecksumUrl = asset.TryGetProperty("browser_download_url", out JsonElement csUrlEl) ? csUrlEl.GetString() : null;
                    }
                }
            }

            if (string.IsNullOrEmpty(_remoteDownloadUrl))
                return;

            _remoteTagName = tagName;

            string remoteVerCore = tagName.TrimStart('v', 'V').Split('-')[0];
            string localVerCore = ClientFileSources.DisplayVersion.TrimStart('v', 'V').Split('-')[0];

            if (Version.TryParse(remoteVerCore, out Version? remoteV) && Version.TryParse(localVerCore, out Version? localV))
                UpdateAvailable = remoteV > localV;
            else
                UpdateAvailable = !string.Equals(remoteVerCore, localVerCore, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            UpdateAvailable = false;
        }
    }

    public async Task DownloadUpdateAsync(IProgress<string>? progress)
    {
        if (string.IsNullOrEmpty(_remoteDownloadUrl))
            await CheckForUpdateAsync();

        if (!UpdateAvailable || string.IsNullOrEmpty(_remoteDownloadUrl))
        {
            progress?.Report("You have the latest version.");
            return;
        }

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

            string? runningAppImage = Environment.GetEnvironmentVariable("APPIMAGE");
            string targetDir = !string.IsNullOrEmpty(runningAppImage) && File.Exists(runningAppImage)
                ? Path.GetDirectoryName(runningAppImage)!
                : AppContext.BaseDirectory;

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("VibeSkua-ClientUpdateService");
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
                    while ((read = await contentStream.ReadAsync(buffer)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read));
                        totalRead += read;
                        if (totalBytes > 0)
                            progress?.Report($"Downloading... {(int)((totalRead * 100) / totalBytes)}%");
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
                            string candidate = rawLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
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
                    progress?.Report("Verifying SHA-256 integrity...");
                    await using FileStream verifyStream = File.OpenRead(tarGzPath);
                    string actualHex = Convert.ToHexString(await SHA256.HashDataAsync(verifyStream));
                    if (!actualHex.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"SHA-256 verification failed (expected {expectedSha256}, got {actualHex}).");
                }
            }

            progress?.Report("Extracting update...");
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

            string[] extractedAppImages = Directory.GetFiles(extractDir, "*.AppImage", SearchOption.AllDirectories);
            if (extractedAppImages.Length == 0)
                throw new InvalidOperationException("No .AppImage binaries found inside the update package.");

            foreach (string extracted in extractedAppImages)
            {
                try
                {
                    File.SetUnixFileMode(extracted,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch { }

                string destination = Path.Combine(targetDir, Path.GetFileName(extracted));
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
                    File.Copy(extracted, destination, overwrite: true);
                    try { File.Delete(extracted); } catch { }
                }

                try { if (File.Exists(backupOld)) File.Delete(backupOld); } catch { }
            }

            try { Directory.Delete(tempDir, true); } catch { }

            progress?.Report("Installing update and restarting...");
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
        catch (Exception e)
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            progress?.Report("Error while updating.");
            _dialogService.ShowMessageBox($"Error Message:\r\n{e.Message}", "Update Error");
        }
    }
}

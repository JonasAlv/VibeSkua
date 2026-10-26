using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.App.Avalonia.Services;

public class FileDialogService : IFileDialogService
{
    private const string DefaultFilter = "Text Files (*.txt)|*.txt";

    private static IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var activeWindow = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
            return activeWindow?.StorageProvider;
        }
        return null;
    }

    private static List<FilePickerFileType> ParseFilters(string? filterString)
    {
        var result = new List<FilePickerFileType>();
        if (string.IsNullOrWhiteSpace(filterString))
            return result;

        var parts = filterString.Split('|');
        for (int i = 0; i < parts.Length - 1; i += 2)
        {
            string name = parts[i];
            string patterns = parts[i + 1];
            var extensions = patterns.Split(';', ',')
                .Select(p => p.Trim())
                .Select(p => p.StartsWith("*.") ? p.Substring(2) : p)
                .Where(p => p != "*")
                .ToList();

            result.Add(new FilePickerFileType(name)
            {
                Patterns = extensions.Select(ext => "*." + ext).ToList()
            });
        }
        return result;
    }

    public string? OpenFile() => OpenFile(ClientFileSources.SkuaDIR, "All Files (*.*)|*.*");

    public string? OpenFile(string filters) => OpenFile(ClientFileSources.SkuaDIR, filters);

    public string? OpenFile(string initialDirectory, string filters)
    {
        return Task.Run(async () =>
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var provider = GetStorageProvider();
                if (provider == null) return null;

                var options = new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    FileTypeFilter = ParseFilters(filters),
                    SuggestedStartLocation = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
                        ? await provider.TryGetFolderFromPathAsync(initialDirectory)
                        : null
                };

                var files = await provider.OpenFilePickerAsync(options);
                return files.FirstOrDefault()?.TryGetLocalPath();
            });
        }).GetAwaiter().GetResult();
    }

    public string? OpenFolder() => OpenFolder(ClientFileSources.SkuaDIR);

    public string? OpenFolder(string initialDirectory)
    {
        return OpenFolderAsync(initialDirectory).GetAwaiter().GetResult();
    }

    public Task<string?> OpenFolderAsync() => OpenFolderAsync(ClientFileSources.SkuaDIR);

    public async Task<string?> OpenFolderAsync(string initialDirectory)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var provider = GetStorageProvider();
            if (provider == null) return null;

            var options = new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                SuggestedStartLocation = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
                    ? await provider.TryGetFolderFromPathAsync(initialDirectory)
                    : null
            };

            var folders = await provider.OpenFolderPickerAsync(options);
            return folders.FirstOrDefault()?.TryGetLocalPath();
        });
    }

    public IEnumerable<string>? OpenText()
    {
        string? path = OpenFile(ClientFileSources.SkuaDIR, DefaultFilter);
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : File.ReadAllLines(path);
    }

    public string? Save() => Save(ClientFileSources.SkuaDIR, DefaultFilter);

    public string? Save(string filters) => Save(ClientFileSources.SkuaDIR, filters);

    public string? Save(string initialDirectory, string filters)
    {
        return Task.Run(async () =>
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var provider = GetStorageProvider();
                if (provider == null) return null;

                var options = new FilePickerSaveOptions
                {
                    FileTypeChoices = ParseFilters(filters),
                    SuggestedStartLocation = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
                        ? await provider.TryGetFolderFromPathAsync(initialDirectory)
                        : null
                };

                var file = await provider.SaveFilePickerAsync(options);
                return file?.TryGetLocalPath();
            });
        }).GetAwaiter().GetResult();
    }

    public void SaveText(string contents)
    {
        string? path = Save();
        if (!string.IsNullOrWhiteSpace(path))
            File.WriteAllText(path, contents);
    }

    public void SaveText(IEnumerable<string> contents)
    {
        string? path = Save();
        if (!string.IsNullOrWhiteSpace(path))
            File.WriteAllLines(path, contents);
    }
}

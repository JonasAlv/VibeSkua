using System.Diagnostics;

namespace Skua.Core.Models;

public static class ConnectedAccounts
{
    /// <summary>
    /// The account logged in here followed by the ones logged in on the other open tabs.
    /// Each tab writes its logged in username to %TEMP%\VibeSkuaTabs\{pid}.txt (MainWindow).
    /// </summary>
    public static List<string> Get(string? self)
    {
        List<string> others = new();
        string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
        if (Directory.Exists(dir))
        {
            foreach (string file in Directory.GetFiles(dir, "*.txt"))
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int pid) || pid == Environment.ProcessId || !IsProcessAlive(pid))
                    continue;
                try
                {
                    string name = File.ReadAllText(file).Trim();
                    if (!string.IsNullOrEmpty(name))
                        others.Add(name);
                }
                catch { }
            }
        }

        List<string> accounts = new();
        if (!string.IsNullOrEmpty(self))
            accounts.Add(self);
        accounts.AddRange(others
            .Where(n => !accounts.Contains(n, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        return accounts;
    }

    /// <returns>Pid of the other open tab logged in to <paramref name="account"/>, or null if none.</returns>
    public static int? FindTab(string account)
    {
        string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
        if (!Directory.Exists(dir))
            return null;

        foreach (string file in Directory.GetFiles(dir, "*.txt"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int pid) || pid == Environment.ProcessId || !IsProcessAlive(pid))
                continue;
            try
            {
                if (File.ReadAllText(file).Trim().Equals(account, StringComparison.OrdinalIgnoreCase))
                    return pid;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Asks the tab logged in to <paramref name="account"/> to enhance its equipped class (picked up by <see cref="TakeEnhanceRequest"/>).
    /// </summary>
    /// <returns><see langword="false"/> if no open tab is logged in to it.</returns>
    public static bool RequestEnhance(string account)
    {
        if (FindTab(account) is not int pid)
            return false;
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "VibeSkuaTabs", $"{pid}.enhance"), account);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <returns><see langword="true"/> once per enhance request sent to this tab.</returns>
    public static bool TakeEnhanceRequest()
    {
        string file = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs", $"{Environment.ProcessId}.enhance");
        if (!File.Exists(file))
            return false;
        try
        {
            File.Delete(file);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}

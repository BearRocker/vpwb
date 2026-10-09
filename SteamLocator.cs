using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace WukongBench;

public static class SteamLocator
{
    public static string? FindSteamRoot()
    {
        foreach (var (hive, key, val) in new[] {
            (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath") })
        {
            using var k = hive.OpenSubKey(key);
            if (k?.GetValue(val) is string s && Directory.Exists(s)) return Path.GetFullPath(s);
        }
        return null;
    }

    public static string? FindSteamExe()
    {
        var root = FindSteamRoot();
        var exe = root == null ? null : Path.Combine(root, "steam.exe");
        return exe != null && File.Exists(exe) ? exe : null;
    }

    public static IEnumerable<string> Libraries(string steamRoot)
    {
        yield return steamRoot;
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
        {
            var p = m.Groups[1].Value.Replace(@"\\", @"\");
            if (!string.Equals(Path.GetFullPath(p), steamRoot, StringComparison.OrdinalIgnoreCase)) yield return p;
        }
    }

    /// <summary>Каталог установки приложения или null, если оно не установлено.</summary>
    public static string? FindAppDir(int appId, IEnumerable<string> extraDirs)
    {
        var root = FindSteamRoot();
        if (root != null)
            foreach (var lib in Libraries(root))
            {
                var acf = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
                if (!File.Exists(acf)) continue;
                var m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"");
                if (!m.Success) continue;
                var dir = Path.Combine(lib, "steamapps", "common", m.Groups[1].Value);
                if (Directory.Exists(dir)) return dir;
            }
        return extraDirs.FirstOrDefault(Directory.Exists);
    }
}

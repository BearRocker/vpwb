using System.Drawing;
using System.Text;

namespace WukongBench;

public static class Program
{
    static readonly string Help = """
        WukongBench - automated Black Myth: Wukong Benchmark Tool runner (CPU pass + GPU pass)

        Usage: WukongBench [options]
          --only cpu|gpu       run a single pass
          --out <dir>          output directory (default: .\results\<timestamp>)
          --settings <file>    settings/presets file (default: settings.json next to the exe)
          --dry-run            write the presets into the tool's config and print them, but do not start the game
          --info               print system info and Steam/game detection, then exit
          --ocr-image <png>    run the result-screen parser on a screenshot (calibration)
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string? Opt(string n) { var i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Help); return 0; }

        if (Opt("--ocr-image") is string img)
        {
            using var bmp = new Bitmap(img);
            var words = await Ocr.ReadScreenAsync(bmp, ResultParser.IsResultScreen(bmp.Width, bmp.Height));
            var (m, settings) = ResultParser.ParseResultScreen(words, bmp.Width, bmp.Height);
            Console.WriteLine(m == null ? "result screen NOT recognised"
                : $"avg {m.AvgFps}  max {m.MaxFps}  min {m.MinFps}  p5 {m.P5Fps}  vram {m.VramGb}");
            if (args.Contains("--debug"))
                foreach (var (y, t) in ResultParser.Rows(words, w => w.X < 0.30 * bmp.Width)) Console.WriteLine($"  L y={y:0} {t}");
            Console.WriteLine("main menu: " + ResultParser.LooksLikeMainMenu(words, bmp.Width, bmp.Height));
            settings.ForEach(s => Console.WriteLine("  | " + s));
            return m == null ? 1 : 0;
        }

        var s = Settings.Load(Opt("--settings") ?? Path.Combine(AppContext.BaseDirectory, "settings.json"));
        var sys = SystemInfo.Collect();
        var appDir = SteamLocator.FindAppDir(s.AppId, s.ExtraSearchDirs);
        Console.WriteLine(Reporter.SystemText(sys));
        Console.WriteLine($"Steam:      {SteamLocator.FindSteamRoot() ?? "NOT FOUND"}");
        Console.WriteLine($"Game dir:   {appDir ?? "NOT INSTALLED"}");
        if (args.Contains("--info")) return 0;

        if (appDir == null)
        {
            Console.Error.WriteLine($"Black Myth: Wukong Benchmark Tool (Steam appid {s.AppId}) is not installed: https://store.steampowered.com/app/{s.AppId}/");
            return 2;
        }
        if (SteamLocator.FindSteamExe() == null) { Console.Error.WriteLine("steam.exe not found."); return 2; }

        var outDir = Opt("--out") ?? Path.Combine("results", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(outDir);
        var only = Opt("--only")?.ToUpperInvariant();
        bool dry = args.Contains("--dry-run");

        var (dw, dh) = SystemInfo.PrimaryResolution();
        bool rtx = SystemInfo.NvidiaRtx(sys);
        Console.WriteLine($"NVIDIA RTX (full ray tracing available): {rtx}\n");

        var report = new Report { System = sys };
        using var cfg = new ConfigApplier(appDir);
        var runner = new GameRunner(s, outDir);
        Console.CancelKeyPress += (_, _) => cfg.Restore();   // никогда не оставляем настройки пользователя перезаписанными

        foreach (var name in new[] { "CPU", "GPU" })
        {
            if (only != null && only != name) continue;
            var preset = s.Presets[name];
            var (w, h) = ParseRes(preset.Resolution, dw, dh);
            var r = await runner.RunAsync(name, preset, w, h, rtx, cfg, dry);
            if (name == "CPU") report.Cpu = r; else report.Gpu = r;
        }

        Reporter.Write(report, outDir);
        Console.WriteLine(Reporter.ReportText(report));
        Console.WriteLine($"Saved to {Path.GetFullPath(outDir)}");
        return (report.Cpu?.Error ?? report.Gpu?.Error) == null ? 0 : 1;
    }

    static (int, int) ParseRes(string r, int dw, int dh)
    {
        if (r.Equals("native", StringComparison.OrdinalIgnoreCase)) return (dw, dh);
        var p = r.Split('x', 'X');
        return (int.Parse(p[0]), int.Parse(p[1]));
    }
}

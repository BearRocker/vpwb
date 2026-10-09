using System.Text;
using System.Text.Json;

namespace WukongBench;

public static class Reporter
{
    public static string SystemText(SystemSnapshot s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"OS:         {s.Os}");
        sb.AppendLine($"CPU:        {s.Cpu} ({s.CpuCores})");
        foreach (var g in s.Gpus) sb.AppendLine($"GPU:        {g}");
        sb.AppendLine($"RAM:        {s.Ram}");
        sb.AppendLine($"Board:      {s.Motherboard}");
        sb.AppendLine($"Display:    {s.Display}");
        return sb.ToString().TrimEnd();
    }

    static string Metrics(RunResult? r)
    {
        if (r == null) return "  (not run)";
        if (r.Metrics == null) return $"  FAILED: {r.Error}";
        var m = r.Metrics;
        var sb = new StringBuilder();
        sb.AppendLine($"  Average FPS       {m.AvgFps}");
        sb.AppendLine($"  Maximum FPS       {m.MaxFps}");
        sb.AppendLine($"  Minimum FPS       {m.MinFps}");
        if (m.P5Fps != null) sb.AppendLine($"  5th percentile    {m.P5Fps}");
        if (m.VramGb != null) sb.AppendLine($"  VRAM used         {m.VramGb} GB");
        sb.Append($"  run time          {r.DurationSec:0} s");
        return sb.ToString();
    }

    static string Settings(RunResult? r)
    {
        if (r == null) return "  (not run)";
        var sb = new StringBuilder();
        foreach (var kv in r.AppliedSettings) sb.AppendLine($"  {kv.Key,-20} {kv.Value}");
        if (r.ToolSettingsText.Count > 0)
            sb.AppendLine("  как показано на экране результатов инструмента:").Append(string.Join(Environment.NewLine, r.ToolSettingsText.Select(l => "    " + l)));
        return sb.ToString().TrimEnd();
    }

    public static string ReportText(Report r)
    {
        var sb = new StringBuilder();
        string line = new('=', 64);
        sb.AppendLine(line).AppendLine($"Black Myth: Wukong Benchmark - {r.Date:yyyy-MM-dd HH:mm}").AppendLine(line);
        sb.AppendLine("SYSTEM").AppendLine(SystemText(r.System)).AppendLine();
        sb.AppendLine("CPU TEST RESULT").AppendLine(Metrics(r.Cpu)).AppendLine();
        sb.AppendLine("GPU TEST RESULT").AppendLine(Metrics(r.Gpu)).AppendLine();
        sb.AppendLine("CPU TEST SETTINGS").AppendLine(Settings(r.Cpu)).AppendLine();
        sb.AppendLine("GPU TEST SETTINGS").AppendLine(Settings(r.Gpu));
        return sb.ToString();
    }

    public static void Write(Report r, string dir)
    {
        File.WriteAllText(Path.Combine(dir, "report.txt"), ReportText(r), Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "report.json"),
            JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), Encoding.UTF8);
    }
}

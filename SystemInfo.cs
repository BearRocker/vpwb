using System.Management;
using System.Text.RegularExpressions;

namespace WukongBench;

public static class SystemInfo
{
    static List<ManagementBaseObject> Query(string q)
    {
        try { return new ManagementObjectSearcher(q).Get().Cast<ManagementBaseObject>().ToList(); }
        catch { return new(); }
    }

    public static (int w, int h) PrimaryResolution()
    {
        var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        return (b.Width, b.Height);   // процесс PerMonitorV2 DPI-aware, поэтому это физические пиксели
    }

    public static SystemSnapshot Collect()
    {
        var s = new SystemSnapshot();
        var os = Query("SELECT Caption, Version FROM Win32_OperatingSystem").FirstOrDefault();
        if (os != null) s.Os = $"{os["Caption"]} ({os["Version"]})";

        var cpu = Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor").FirstOrDefault();
        if (cpu != null)
        {
            s.Cpu = ((string?)cpu["Name"] ?? "").Trim();
            s.CpuCores = $"{cpu["NumberOfCores"]}C/{cpu["NumberOfLogicalProcessors"]}T, {cpu["MaxClockSpeed"]} MHz";
        }

        foreach (var g in Query("SELECT Name, DriverVersion FROM Win32_VideoController"))
        {
            var name = (string?)g["Name"] ?? "";
            if (Regex.IsMatch(name, "Virtual|Basic Render|Remote", RegexOptions.IgnoreCase)) continue;
            s.Gpus.Add($"{name} (driver {g["DriverVersion"]})");
        }

        var mem = Query("SELECT Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
        if (mem.Count > 0)
        {
            double gb = mem.Sum(m => Convert.ToDouble(m["Capacity"])) / (1024.0 * 1024 * 1024);
            int speed = mem.Max(m => Convert.ToInt32(m["ConfiguredClockSpeed"] ?? m["Speed"] ?? 0));
            s.Ram = $"{gb:0} GB ({mem.Count} module(s), {speed} MT/s)";
        }

        var (w, h) = PrimaryResolution();
        s.Display = $"{w}x{h}";
        var bb = Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        if (bb != null) s.Motherboard = $"{bb["Manufacturer"]} {bb["Product"]}".Trim();
        return s;
    }

    /// <summary>Опция инструмента «полная трассировка лучей» (path tracing) есть только у NVIDIA.</summary>
    public static bool NvidiaRtx(SystemSnapshot s) => s.Gpus.Any(g =>
        Regex.IsMatch(g, @"NVIDIA.*\bRTX\b|GeForce\s+RTX", RegexOptions.IgnoreCase));
}

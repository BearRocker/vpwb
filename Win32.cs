using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WukongBench;

public static class Win32
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] static extern bool GetClientRect(nint h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(nint h, ref POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, nint extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, nint extra);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    static readonly Dictionary<string, byte> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D, ["Space"] = 0x20, ["Tab"] = 0x09, ["Esc"] = 0x1B,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
    };

    public static void Focus(nint hwnd)
    {
        ShowWindow(hwnd, 9);                                      // SW_RESTORE
        keybd_event(0x12, 0, 0, 0); keybd_event(0x12, 0, 2, 0);   // короткое нажатие Alt снимает блокировку смены окна переднего плана
        SetForegroundWindow(hwnd);
    }

    static byte ToVk(string name)
    {
        if (Keys.TryGetValue(name, out var k)) return k;
        if (name.Length >= 2 && char.ToUpperInvariant(name[0]) == 'F' && int.TryParse(name[1..], out var n) && n is >= 1 and <= 12)
            return (byte)(0x6F + n);
        if (name.Length == 1) return (byte)char.ToUpperInvariant(name[0]);
        throw new ArgumentException($"Unknown key '{name}'");
    }

    public static void PressKey(string name)
    {
        var vk = ToVk(name);
        keybd_event(vk, 0, 0, 0); Thread.Sleep(60); keybd_event(vk, 0, 2, 0);
    }

    /// <summary>Клик в точке, заданной относительно (0..1) клиентской области окна.</summary>
    public static void Click(nint hwnd, double rx, double ry)
    {
        GetClientRect(hwnd, out var r);
        var p = new POINT { X = (int)(r.R * rx), Y = (int)(r.B * ry) };
        ClientToScreen(hwnd, ref p);
        SetCursorPos(p.X, p.Y); Thread.Sleep(80);
        mouse_event(0x02, 0, 0, 0, 0); Thread.Sleep(60); mouse_event(0x04, 0, 0, 0, 0);
    }

    public static Bitmap CaptureClient(nint hwnd)
    {
        GetClientRect(hwnd, out var r);
        var p = new POINT();
        ClientToScreen(hwnd, ref p);
        var bmp = new Bitmap(Math.Max(1, r.R), Math.Max(1, r.B), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(p.X, p.Y, 0, 0, bmp.Size);
        return bmp;
    }

    /// <summary>Завершает процесс и его дочерние; если Process.Kill не сработал (ошибки доступа / перебора дерева), использует taskkill.</summary>
    public static void KillTree(Process p)
    {
        try { if (p.HasExited) return; p.Kill(true); return; } catch { }
        try
        {
            using var t = Process.Start(new ProcessStartInfo("taskkill", $"/F /T /PID {p.Id}") { CreateNoWindow = true, UseShellExecute = false });
            t?.WaitForExit(5000);
        }
        catch { }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class DotSnap
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    static IntPtr dot;
    static string Classify()
    {
        RECT rc; GetWindowRect(dot, out rc);
        IntPtr dc = GetDC(IntPtr.Zero);
        uint px = GetPixel(dc, (rc.l + rc.r) / 2, (rc.t + rc.b) / 2);
        ReleaseDC(IntPtr.Zero, dc);
        int r = (int)(px & 0xFF), g = (int)((px >> 8) & 0xFF), b = (int)((px >> 16) & 0xFF);
        if (b > r + 40 && b > g + 40) return "BLUE(custom-img)";
        if (g > r + 20 && g > b + 20) return "GREEN(default-zh)";
        if (r > g + 20 && r > b + 20) return "RED(default-en)";
        return string.Format("OTHER {0},{1},{2}", r, g, b);
    }

    static void TapShift()
    {
        keybd_event(0x10, 0, 0, IntPtr.Zero);
        Thread.Sleep(150);
        keybd_event(0x10, 0, 2, IntPtr.Zero);
        Thread.Sleep(500);
    }

    static void WritePng(Color c)
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\ImeCursorDot";
        System.IO.Directory.CreateDirectory(dir);
        using (Bitmap bmp = new Bitmap(128, 128, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp)) g.Clear(c);
            bmp.Save(dir + "\\zh_custom.png", ImageFormat.Png);
            bmp.Save(dir + "\\en_custom.png", ImageFormat.Png);
        }
    }

    [STAThread]
    static void Main()
    {
        uint target = 0;
        foreach (Process p in Process.GetProcessesByName("ImeCursorDot")) { target = (uint)p.Id; break; }
        if (target == 0) { Console.WriteLine("NOT RUNNING"); return; }
        EnumWindows((h, l) =>
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == target && IsWindowVisible(h))
            {
                RECT rc; GetWindowRect(h, out rc);
                if (rc.r - rc.l >= 4 && rc.r - rc.l <= 50) { dot = h; return false; }
            }
            return true;
        }, IntPtr.Zero);
        if (dot == IntPtr.Zero) { Console.WriteLine("NO DOT"); return; }

        Screen pri = Screen.PrimaryScreen;
        SetCursorPos(pri.Bounds.Width / 2, pri.Bounds.Height / 2);
        Thread.Sleep(400);

        string dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\ImeCursorDot";
        try { System.IO.File.Delete(dir + "\\zh_custom.png"); System.IO.File.Delete(dir + "\\en_custom.png"); } catch (Exception) { }
        TapShift();
        string s0 = Classify();

        WritePng(Color.Blue);
        TapShift();
        string s1 = Classify();

        try { System.IO.File.Delete(dir + "\\zh_custom.png"); System.IO.File.Delete(dir + "\\en_custom.png"); } catch (Exception) { }
        TapShift();
        string s2 = Classify();

        Console.WriteLine("default=" + s0 + "  custom=" + s1 + "  restored=" + s2);
        bool pass = s1 == "BLUE(custom-img)"
            && (s0.StartsWith("GREEN") || s0.StartsWith("RED"))
            && (s2.StartsWith("GREEN") || s2.StartsWith("RED"));
        Console.WriteLine(pass ? "PASS: custom image renders and default restores" : "FAIL");
    }
}

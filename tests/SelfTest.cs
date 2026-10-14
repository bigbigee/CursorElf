using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

class SelfTest
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    static IntPtr dotHwnd;

    static string Classify()
    {
        RECT rc;
        GetWindowRect(dotHwnd, out rc);
        IntPtr dc = GetDC(IntPtr.Zero);
        uint px = GetPixel(dc, (rc.l + rc.r) / 2, (rc.t + rc.b) / 2);
        ReleaseDC(IntPtr.Zero, dc);
        int r = (int)(px & 0xFF), g = (int)((px >> 8) & 0xFF), b = (int)((px >> 16) & 0xFF);
        if (g > r + 20 && g > b + 20) return "GREEN(中文)";
        if (r > g + 20 && r > b + 20) return "RED(英文)";
        return string.Format("OTHER rgb={0},{1},{2}", r, g, b);
    }

    static void Main()
    {
        uint target = 0;
        foreach (Process p in Process.GetProcessesByName("ImeCursorDot")) { target = (uint)p.Id; break; }
        if (target == 0) { Console.WriteLine("ImeCursorDot NOT RUNNING"); return; }

        EnumWindows((h, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == target && IsWindowVisible(h))
            {
                RECT rc;
                GetWindowRect(h, out rc);
                if (rc.r - rc.l <= 12 && rc.b - rc.t <= 12) { dotHwnd = h; return false; }
            }
            return true;
        }, IntPtr.Zero);

        if (dotHwnd == IntPtr.Zero) { Console.WriteLine("DOT WINDOW NOT FOUND"); return; }

        string before = Classify();
        keybd_event(0x10, 0, 0, IntPtr.Zero);           // Shift down
        Thread.Sleep(60);
        keybd_event(0x10, 0, 2, IntPtr.Zero);           // Shift up
        Thread.Sleep(400);
        string after = Classify();
        Console.WriteLine("before=" + before);
        Console.WriteLine("after =" + after);
        Console.WriteLine(before != after ? "PASS: Shift tap changed the dot color" : "FAIL: dot color did not change");
    }
}

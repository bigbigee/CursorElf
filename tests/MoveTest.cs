using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class MoveTest
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }

    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);

    static IntPtr FindDot(uint target)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == target && IsWindowVisible(h))
            {
                RECT rc; GetWindowRect(h, out rc);
                int w = rc.r - rc.l, hh = rc.b - rc.t;
                if (w >= 4 && w <= 50 && hh >= 4 && hh <= 50) found = h;
            }
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        return found;
    }

    [STAThread]
    static void Main()
    {
        SetProcessDPIAware();
        uint target = 0;
        foreach (Process p in Process.GetProcessesByName("ImeCursorDot")) { target = (uint)p.Id; break; }
        if (target == 0) { Console.WriteLine("ImeCursorDot NOT RUNNING"); return; }
        IntPtr dot = FindDot(target);
        if (dot == IntPtr.Zero) { Console.WriteLine("DOT WINDOW NOT FOUND"); return; }

        POINT home; GetCursorPos(out home);

        var pts = new List<Point>();
        foreach (Screen s in Screen.AllScreens)
        {
            Rectangle b = s.Bounds;
            Console.WriteLine("monitor: " + b + (s.Primary ? " PRIMARY" : " secondary"));
            pts.Add(new Point(b.Left + b.Width / 4, b.Top + b.Height / 3));
            pts.Add(new Point(b.Left + b.Width / 2, b.Top + b.Height / 2));
            pts.Add(new Point(b.Left + b.Width * 3 / 4, b.Top + b.Height * 2 / 3));
        }

        int baseDx = 0, baseDy = 0; bool first = true; int maxDev = 0;
        foreach (Point pt in pts)
        {
            SetCursorPos(pt.X, pt.Y);
            Thread.Sleep(150);
            POINT c; GetCursorPos(out c);
            RECT rc; GetWindowRect(dot, out rc);
            int dx = rc.l - c.x, dy = rc.t - c.y;
            int dev = 0;
            if (first) { baseDx = dx; baseDy = dy; first = false; }
            else dev = Math.Max(Math.Abs(dx - baseDx), Math.Abs(dy - baseDy));
            if (dev > maxDev) maxDev = dev;
            Console.WriteLine("cursor=" + c.x + "," + c.y + " dotOff=" + dx + "," + dy + " dev=" + dev);
        }

        SetCursorPos(home.x, home.y);
        Console.WriteLine(maxDev <= 2 ? "PASS: dot offset consistent across all screens (maxDev=" + maxDev + ")"
                                      : "FAIL: offset drifts between screens (maxDev=" + maxDev + ")");
    }
}

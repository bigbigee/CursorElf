using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

class FollowCheck
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
    [StructLayout(LayoutKind.Sequential)] struct PT { public int x, y; }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out PT p);

    static IntPtr dot;
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
        for (int i = 0; i < 6; i++)
        {
            PT c; GetCursorPos(out c);
            RECT rc; GetWindowRect(dot, out rc);
            Console.WriteLine("cursor=" + c.x + "," + c.y + " dot=" + rc.l + "," + rc.t +
                " off=" + (rc.l - c.x) + "," + (rc.t - c.y));
            Thread.Sleep(400);
        }
    }
}

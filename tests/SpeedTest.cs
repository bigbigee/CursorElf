using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

class SpeedTest
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

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

        // Park the cursor, let the dot settle onto it.
        SetCursorPos(200, 200);
        Thread.Sleep(1500);

        // Teleport far away; sample the dot immediately, then after it converges.
        int bx = 1400, by = 700;
        SetCursorPos(bx, by);
        Thread.Sleep(40);
        int lagDist = DistanceToTarget(bx, by);
        Thread.Sleep(2500);
        int settledDist = DistanceToTarget(bx, by);

        Console.WriteLine("immediate lag = " + lagDist + " px");
        Console.WriteLine("after settle  = " + settledDist + " px");
        bool pass = lagDist > 60 && settledDist <= 25;
        Console.WriteLine(pass ? "PASS: dot eases toward cursor then converges" : "FAIL");
    }

    // Distance from the dot's current top-left to where it should sit for a bottom-right
    // offset of (dx=12, dy=12) relative to the cursor.
    static int DistanceToTarget(int cx, int cy)
    {
        RECT rc; GetWindowRect(dot, out rc);
        int wantX = cx + 12, wantY = cy + 12;
        int dx = rc.l - wantX, dy = rc.t - wantY;
        return (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
    }
}

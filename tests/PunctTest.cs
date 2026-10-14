using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class PunctTest
{
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint vk, uint mapType);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }

    static string ColorAt(IntPtr hwnd)
    {
        RECT r; GetWindowRect(hwnd, out r);
        int cx = (r.l + r.r) / 2, cy = (r.t + r.b) / 2;
        IntPtr dc = GetDC(IntPtr.Zero);
        int rgb = GetPixel(dc, cx, cy);
        ReleaseDC(IntPtr.Zero, dc);
        int rr = rgb & 0xFF, gg = (rgb >> 8) & 0xFF, bb = (rgb >> 16) & 0xFF;
        if (gg > rr + 20 && gg > bb + 20) return "GREEN";
        if (rr > gg + 20 && rr > bb + 20) return "RED";
        return "OTHER(" + rr + "," + gg + "," + bb + ")";
    }
    [DllImport("gdi32.dll")] static extern int GetPixel(IntPtr dc, int x, int y);

    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);

    static IntPtr Dot()
    {
        uint target = 0;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("ImeCursorDot"))
        { target = (uint)p.Id; break; }
        if (target == 0) return IntPtr.Zero;
        IntPtr found = IntPtr.Zero;
        EnumWindowsProc cb = delegate (IntPtr h, IntPtr lp)
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == target && IsWindowVisible(h))
            {
                RECT r; GetWindowRect(h, out r);
                int w = r.r - r.l, hh = r.b - r.t;
                if (w >= 4 && w <= 40 && hh >= 4 && hh <= 40) found = h;
            }
            return true;
        };
        EnumWindows(cb, IntPtr.Zero);
        return found;
    }
    delegate bool EnumWindowsProc(IntPtr h, IntPtr lp);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

    static void Tap(byte vk, int holdMs)
    {
        keybd_event(vk, (byte)MapVirtualKey(vk, 0), 0, IntPtr.Zero);
        Thread.Sleep(holdMs);
        keybd_event(vk, (byte)MapVirtualKey(vk, 0), 2, IntPtr.Zero);
    }

    [STAThread]
    static void Main()
    {
        IntPtr hwnd = Dot();
        if (hwnd == IntPtr.Zero) { Console.WriteLine("NO DOT WINDOW"); return; }
        Thread.Sleep(300);
        string before = ColorAt(hwnd);

        // Scenario 1: Shift held + punctuation typed (Shift+, etc.) — must NOT toggle.
        keybd_event(0x10, (byte)MapVirtualKey(0x10, 0), 0, IntPtr.Zero); // shift down
        Thread.Sleep(120);
        Tap(0xBC, 120);   // comma while shift held
        Thread.Sleep(120);
        keybd_event(0x10, (byte)MapVirtualKey(0x10, 0), 2, IntPtr.Zero); // shift up
        Thread.Sleep(500);
        string after1 = ColorAt(hwnd);
        Console.WriteLine("punct-chord: before=" + before + " after=" + after1 +
            (after1 == before ? "  PASS(no toggle)" : "  FAIL(toggled!)"));

        // Scenario 2: pure Shift tap — must toggle.
        Tap(0x10, 150);
        Thread.Sleep(500);
        string after2 = ColorAt(hwnd);
        Console.WriteLine("shift-tap:   before=" + after1 + " after=" + after2 +
            (after2 != after1 ? "  PASS(toggled)" : "  FAIL(no toggle)"));
    }
}

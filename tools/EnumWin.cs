using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

class EnumWin
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT rc);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    struct RECT { public int L, T, R, B; }
    struct POINT { public int X, Y; }

    static void Main(string[] args)
    {
        uint target = uint.Parse(args[0]);
        var list = new List<string>();
        EnumWindows((h, l) =>
        {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p == target && IsWindowVisible(h))
            {
                RECT rc;
                GetWindowRect(h, out rc);
                list.Add(string.Format("hwnd={0:X} ex=0x{1:X6} size={2}x{3} at=({4},{5})",
                    h, GetWindowLong(h, -20), rc.R - rc.L, rc.B - rc.T, rc.L, rc.T));
            }
            return true;
        }, IntPtr.Zero);
        POINT c;
        GetCursorPos(out c);
        Console.WriteLine("cursor=(" + c.X + "," + c.Y + ")");
        foreach (var s in list) Console.WriteLine(s);
        if (list.Count == 0) Console.WriteLine("NO VISIBLE WINDOWS");
    }
}

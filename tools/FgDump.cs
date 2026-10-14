using System;
using System.Runtime.InteropServices;
using System.Text;

class FgDump
{
    [StructLayout(LayoutKind.Sequential)]
    struct GUITHREADINFO { public int cbSize, flags; public IntPtr a, f, c, m, mv, caret; public int l, t, r, b; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("imm32.dll", SetLastError = true)] static extern IntPtr ImmGetContext(IntPtr h);
    [DllImport("imm32.dll")] static extern bool ImmGetConversionStatus(IntPtr ctx, out uint conv, out uint sent);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr h, IntPtr ctx);

    static string Cls(IntPtr h)
    {
        var sb = new StringBuilder(64);
        GetClassName(h, sb, 64);
        return sb.ToString();
    }

    static void Main()
    {
        IntPtr fg = GetForegroundWindow();
        uint pid;
        uint tid = GetWindowThreadProcessId(fg, out pid);
        int lang = (int)(GetKeyboardLayout(tid).ToInt64() & 0xFFFF);
        GUITHREADINFO g = new GUITHREADINFO();
        g.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
        IntPtr focus = (GetGUIThreadInfo(tid, ref g) && g.f != IntPtr.Zero) ? g.f : fg;
        IntPtr ctx = ImmGetContext(focus);
        string conv = "ctx=NULL err=" + Marshal.GetLastWin32Error();
        if (ctx != IntPtr.Zero)
        {
            uint c, s;
            conv = ImmGetConversionStatus(ctx, out c, out s) ? "0x" + c.ToString("X8") : "queryfail";
            ImmReleaseContext(focus, ctx);
        }
        Console.WriteLine("fg=" + Cls(fg) + " focus=" + Cls(focus) + " lang=0x" + lang.ToString("X4") + " conv=" + conv);
    }
}

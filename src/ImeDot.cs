using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ImeCursorDot
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public int biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public BITMAPINFOHEADER Header;
        }

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint idThread);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey,
            ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint iUsage,
            out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("imm32.dll")] public static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")] public static extern bool ImmGetConversionStatus(IntPtr hIMC, out uint fdwConversion, out uint fdwSentence);
        [DllImport("imm32.dll")] public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        public delegate int LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOREDRAW = 0x0008, SWP_NOACTIVATE = 0x0010;

        // Mirrors Microsoft Pinyin's Shift-toggle: on Win11 24H2+ the legacy IMM32
        // API no longer exposes the IME Chinese/English mode, so we track taps ourselves.
        public static volatile bool MirrorChinese = true;
        public static volatile bool MirrorDirty = false;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll")]
        public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);

        const uint IME_CMODE_NATIVE = 0x0001;
        const uint IME_CMODE_NOCONVERSION = 0x40000000;
        const int LANG_CHINESE = 0x04;

        public const int ULW_ALPHA = 0x00000002;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;

        /// <summary>
        /// true = Chinese input, false = English input.
        /// Prefers the IME conversion status of the foreground window;
        /// falls back to the keyboard layout language.
        /// </summary>
        public static bool IsChineseMode()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            uint tid = GetWindowThreadProcessId(fg, IntPtr.Zero);
            int langId = (int)(GetKeyboardLayout(tid).ToInt64() & 0xFFFF);
            bool zhLayout = (langId & 0x3FF) == LANG_CHINESE;

            // The IME context usually attaches to the focused child window, not the top-level one.
            IntPtr target = fg;
            GUITHREADINFO gui = new GUITHREADINFO();
            gui.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
            if (GetGUIThreadInfo(tid, ref gui) && gui.hwndFocus != IntPtr.Zero)
                target = gui.hwndFocus;

            IntPtr ctx = ImmGetContext(target);
            if (ctx != IntPtr.Zero)
            {
                uint conv, sent;
                bool ok = ImmGetConversionStatus(ctx, out conv, out sent);
                ImmReleaseContext(target, ctx);
                if (ok)
                {
                    if ((conv & IME_CMODE_NOCONVERSION) != 0) { MirrorChinese = false; return false; }
                    bool zh = (conv & IME_CMODE_NATIVE) != 0;
                    MirrorChinese = zh;
                    return zh;
                }
            }
            // No legacy IME signal (modern TSF Microsoft Pinyin): layout + Shift mirror.
            return zhLayout && MirrorChinese;
        }

        /// <summary>Render a premultiplied-BGRA circle (or custom image) and push it to a layered window.</summary>
        public static void PaintDot(IntPtr hwnd, int size, bool chinese)
        {
            Image img = Settings.GetCustomImage(chinese);
            Color fill = chinese ? Settings.Zh : Settings.En;
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    RectangleF rect = new RectangleF(1.5f, 1.5f, size - 3f, size - 3f);
                    using (Pen p = new Pen(Color.FromArgb(230, 0, 0, 0), 1f))
                    {
                        if (img != null)
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            if (Settings.Crop == 1)
                            {
                                RectangleF sq = new RectangleF(0.75f, 0.75f, size - 1.5f, size - 1.5f);
                                g.DrawImage(img, sq);
                                g.DrawRectangle(p, sq.X, sq.Y, sq.Width, sq.Height);
                            }
                            else
                            {
                                using (GraphicsPath clip = new GraphicsPath())
                                {
                                    clip.AddEllipse(rect);
                                    g.SetClip(clip);
                                    g.DrawImage(img, rect);
                                    g.ResetClip();
                                }
                                g.DrawEllipse(p, rect);
                            }
                        }
                        else
                        {
                            using (SolidBrush b = new SolidBrush(fill)) g.FillEllipse(b, rect);
                            g.DrawEllipse(p, rect);
                        }
                    }
                }

                BitmapData data = bmp.LockBits(new Rectangle(0, 0, size, size),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = size * size * 4;
                byte[] buf = new byte[bytes];
                Marshal.Copy(data.Scan0, buf, 0, bytes);
                bmp.UnlockBits(data);

                // GDI+ Format32bppArgb is straight alpha (BGRA); UpdateLayeredWindow needs premultiplied.
                for (int i = 0; i < bytes; i += 4)
                {
                    int a = buf[i + 3];
                    if (a == 255) continue;
                    buf[i] = (byte)(buf[i] * a / 255);
                    buf[i + 1] = (byte)(buf[i + 1] * a / 255);
                    buf[i + 2] = (byte)(buf[i + 2] * a / 255);
                }

                IntPtr screenDc = GetDC(IntPtr.Zero);
                IntPtr memDc = CreateCompatibleDC(screenDc);
                BITMAPINFO bmi = new BITMAPINFO();
                bmi.Header.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.Header.biWidth = size;
                bmi.Header.biHeight = -size; // top-down
                bmi.Header.biPlanes = 1;
                bmi.Header.biBitCount = 32;
                bmi.Header.biCompression = 0; // BI_RGB

                IntPtr ppv;
                IntPtr dib = CreateDIBSection(memDc, ref bmi, 0, out ppv, IntPtr.Zero, 0);
                Marshal.Copy(buf, 0, ppv, bytes);
                IntPtr old = SelectObject(memDc, dib);

                POINT src = new POINT { X = 0, Y = 0 };
                SIZE sz = new SIZE { cx = size, cy = size };
                BLENDFUNCTION blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = (byte)(Settings.Opacity * 255 / 100),
                    AlphaFormat = AC_SRC_ALPHA
                };
                UpdateLayeredWindow(hwnd, screenDc, IntPtr.Zero, ref sz, memDc, ref src, 0, ref blend, ULW_ALPHA);

                SelectObject(memDc, old);
                DeleteObject(dib);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        public static Icon MakeIcon(int size, Color fill)
        {
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    RectangleF r = new RectangleF(1, 1, size - 2, size - 2);
                    using (SolidBrush b = new SolidBrush(fill)) g.FillEllipse(b, r);
                    using (Pen p = new Pen(Color.FromArgb(220, 0, 0, 0), 1f)) g.DrawEllipse(p, r);
                }
                // Clone detaches an owning copy; then the raw GDI handle is freed immediately.
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(h))
                        return (Icon)tmp.Clone();
                }
                finally { DestroyIcon(h); }
            }
        }
    }

    static class Settings
    {
        public static Color Zh = Color.FromArgb(34, 178, 92);
        public static Color En = Color.FromArgb(224, 62, 62);
        public static int Size = 10;
        public static int Pos = 0; // 0=右下 1=左下 2=右上 3=左上（相对光标）
        public static int Dx = 10; // 水平间距（像素）
        public static int Dy = 12; // 垂直间距（像素）
        public static int Crop = 0; // 0=圆形 1=正方形（自定义图案裁剪形状）
        public static int Speed = 10; // 跟随速度 1(慢)~10(即时)
        public static int Opacity = 92; // 圆点不透明度百分比 20~100
        public static bool Dance = true; // 闲置跳舞开关（光标 1 分钟不动触发）

        static string DataDir
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\ImeCursorDot"; }
        }

        public static string ImagePath(bool chinese)
        {
            return DataDir + (chinese ? "\\zh_custom.png" : "\\en_custom.png");
        }

        static Bitmap cachedZh, cachedEn;
        static int cachedVersion = -1;
        public static int ImagesVersion = 0;

        public static Bitmap GetCustomImage(bool chinese)
        {
            if (cachedVersion != ImagesVersion)
            {
                if (cachedZh != null) { cachedZh.Dispose(); cachedZh = null; }
                if (cachedEn != null) { cachedEn.Dispose(); cachedEn = null; }
                cachedVersion = ImagesVersion;
            }
            Bitmap cache = chinese ? cachedZh : cachedEn;
            if (cache != null)
            {
                // The user may delete the PNG outside the settings dialog; drop the cache then.
                if (System.IO.File.Exists(ImagePath(chinese))) return cache;
                cache.Dispose();
                if (chinese) cachedZh = null; else cachedEn = null;
            }
            try
            {
                string path = ImagePath(chinese);
                if (!System.IO.File.Exists(path)) return null;
                byte[] data = System.IO.File.ReadAllBytes(path);
                Bitmap loaded;
                // Copy so the bitmap does not depend on the MemoryStream staying alive.
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream(data))
                using (Bitmap t = new Bitmap(ms))
                    loaded = new Bitmap(t);
                if (chinese) cachedZh = loaded; else cachedEn = loaded;
                return loaded;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Copy the user's picture, center-cropped to a square PNG, into the app data dir.</summary>
        public static bool SetImage(bool chinese, string sourceFile)
        {
            try
            {
                using (Bitmap src = new Bitmap(sourceFile))
                {
                    int side = Math.Min(src.Width, src.Height);
                    if (side <= 0) return false;
                    int sx = (src.Width - side) / 2, sy = (src.Height - side) / 2;
                    using (Bitmap dst = new Bitmap(side, side, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(dst))
                            g.DrawImage(src, new Rectangle(0, 0, side, side),
                                new Rectangle(sx, sy, side, side), GraphicsUnit.Pixel);
                        System.IO.Directory.CreateDirectory(DataDir);
                        dst.Save(ImagePath(chinese), ImageFormat.Png);
                    }
                }
                ImagesVersion++;
                return true;
            }
            catch (Exception) { return false; }
        }

        public static void ClearImages()
        {
            try
            {
                string a = ImagePath(true), b = ImagePath(false);
                // Recycle bin first so a mistaken "恢复默认" is recoverable; fall back to delete.
                if (System.IO.File.Exists(a)) DeleteGone(a);
                if (System.IO.File.Exists(b)) DeleteGone(b);
            }
            catch (Exception) { }
            ImagesVersion++;
        }

        static void DeleteGone(string path)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            catch (Exception) { System.IO.File.Delete(path); }
        }

        public static bool HasAnyImage()
        {
            return System.IO.File.Exists(ImagePath(true)) || System.IO.File.Exists(ImagePath(false));
        }

        static string FilePath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"ImeCursorDot\settings.ini");
        }

        public static void Load()
        {
            try
            {
                string file = FilePath();
                if (!System.IO.File.Exists(file)) return;
                foreach (string line in System.IO.File.ReadAllLines(file))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    int num;
                    if (k == "zh") { int argb; if (int.TryParse(v, NumberStyles.HexNumber, null, out argb)) Zh = Color.FromArgb(argb); }
                    else if (k == "en") { int argb; if (int.TryParse(v, NumberStyles.HexNumber, null, out argb)) En = Color.FromArgb(argb); }
                    else if (k == "size") { if (int.TryParse(v, out num) && num >= 4 && num <= 40) Size = num; }
                    else if (k == "pos") { if (int.TryParse(v, out num) && num >= 0 && num <= 3) Pos = num; }
                    else if (k == "dx") { if (int.TryParse(v, out num) && num >= 0 && num <= 200) Dx = num; }
                    else if (k == "dy") { if (int.TryParse(v, out num) && num >= 0 && num <= 200) Dy = num; }
                    else if (k == "crop") { if (int.TryParse(v, out num) && num >= 0 && num <= 1) Crop = num; }
                    else if (k == "speed") { if (int.TryParse(v, out num) && num >= 1 && num <= 10) Speed = num; }
                    else if (k == "opacity") { if (int.TryParse(v, out num) && num >= 20 && num <= 100) Opacity = num; }
                    else if (k == "dance") { if (int.TryParse(v, out num) && num >= 0 && num <= 1) Dance = num == 1; }
                }
            }
            catch (Exception) { }
        }

        public static void Save()
        {
            try
            {
                string file = FilePath();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                string tmp = file + ".tmp";
                System.IO.File.WriteAllText(tmp,
                    "ver=1\r\n" +
                    "zh=" + Zh.ToArgb().ToString("X8") + "\r\n" +
                    "en=" + En.ToArgb().ToString("X8") + "\r\n" +
                    "size=" + Size + "\r\n" +
                    "pos=" + Pos + "\r\n" +
                    "dx=" + Dx + "\r\n" +
                    "dy=" + Dy + "\r\n" +
                    "crop=" + Crop + "\r\n" +
                    "speed=" + Speed + "\r\n" +
                    "opacity=" + Opacity + "\r\n" +
                    "dance=" + (Dance ? "1" : "0") + "\r\n");
                // Atomic replace: a crash mid-write can never truncate the live settings file.
                if (System.IO.File.Exists(file))
                    System.IO.File.Replace(tmp, file, null);
                else
                    System.IO.File.Move(tmp, file);
            }
            catch (Exception) { }
        }
    }

    class OverlayForm : Form
    {
        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ClientSize = new Size(Settings.Size, Settings.Size);
        }
    }

    // Card-style ("简约卡片") design tokens shared by the settings dialog and tray menu.
    static class Pal
    {
        public static readonly Color Bg = Color.FromArgb(251, 250, 245);        // 米白底
        public static readonly Color Card = Color.FromArgb(255, 255, 255);      // 卡片白
        public static readonly Color Border = Color.FromArgb(234, 230, 218);    // 外框
        public static readonly Color Divider = Color.FromArgb(240, 237, 227);   // 分隔线
        public static readonly Color InputBrd = Color.FromArgb(229, 225, 213);  // 控件描边
        public static readonly Color Text = Color.FromArgb(61, 74, 67);         // 主文字
        public static readonly Color TextSub = Color.FromArgb(92, 107, 98);     // 次级文字
        public static readonly Color Title = Color.FromArgb(154, 164, 155);     // 分组标题
        public static readonly Color Green = Color.FromArgb(110, 150, 121);     // 主色浅绿
        public static readonly Color GreenHover = Color.FromArgb(95, 134, 105);
        public static readonly Color Red = Color.FromArgb(217, 140, 124);       // 低饱和红
        public static readonly Color Hover = Color.FromArgb(243, 247, 243);     // 悬停态
        public static readonly Color BtnSoft = Color.FromArgb(237, 242, 236);   // 次级按钮底
        public static readonly Color BtnSoftText = Color.FromArgb(79, 122, 94);
        public static readonly Color BtnSoftHover = Color.FromArgb(226, 235, 227);
        public static readonly Color Groove = Color.FromArgb(239, 236, 226);    // 滑条底槽
        public static readonly Color Sub = Color.FromArgb(143, 180, 155);       // 滑条已填充
        public static readonly Color ToggleOff = Color.FromArgb(216, 212, 200);

        public static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (SolidBrush b = new SolidBrush(c))
                using (GraphicsPath p = RoundPath(r, radius)) g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color c, float width)
        {
            using (Pen p = new Pen(c, width))
                using (GraphicsPath pth = RoundPath(r, radius)) g.DrawPath(p, pth);
        }
    }

    static class MenuIcons
    {
        public static Bitmap Dot(Color c)
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, 4, 4, 8, 8);
            }
            return bmp;
        }

        public static Bitmap Check(bool on)
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(1, 1, 13, 13);
                if (on)
                {
                    Pal.FillRound(g, r, 4, Pal.Green);
                    using (Pen p = new Pen(Color.White, 1.8f))
                        g.DrawLines(p, new Point[] { new Point(4, 8), new Point(7, 11), new Point(12, 5) });
                }
                else Pal.DrawRound(g, r, 4, Pal.InputBrd, 1f);
            }
            return bmp;
        }

        public static Bitmap Glyph(string ch)
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
                TextRenderer.DrawText(g, ch, new Font("Segoe MDL2 Assets", 9f), new Rectangle(0, 0, 16, 16),
                    Pal.TextSub, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return bmp;
        }
    }

    class CardMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Pal.Card)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Pal.DrawRound(e.Graphics, new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 10, Pal.Border, 1f);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Pal.FillRound(e.Graphics, new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2), 8, Pal.Hover);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Pal.Title : (e.Item.Selected ? Pal.Text : Pal.TextSub);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Pal.Divider)) e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Pal.Card)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }
    }

    enum BtnKind { Primary, Ghost, Soft }

    class FlatBtn : Button
    {
        public BtnKind Kind = BtnKind.Ghost;
        bool hover, pressed;

        public FlatBtn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            BackColor = Pal.Card;
            ForeColor = Pal.Text;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Pal.Bg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg, text;
            bool border = false;
            if (Kind == BtnKind.Primary) { bg = hover ? Pal.GreenHover : Pal.Green; text = Color.White; }
            else if (Kind == BtnKind.Soft) { bg = hover ? Pal.BtnSoftHover : Pal.BtnSoft; text = Pal.BtnSoftText; }
            else { bg = hover ? Pal.Hover : Pal.Card; text = Pal.TextSub; border = true; }
            if (pressed) text = Color.FromArgb(180, text.R, text.G, text.B);
            Pal.FillRound(g, r, 8, bg);
            if (border) Pal.DrawRound(g, r, 8, Pal.InputBrd, 1f);
            TextRenderer.DrawText(g, Text, Font, r, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    class SwatchBtn : Button
    {
        bool hover;

        public SwatchBtn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Pal.Bg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Pal.FillRound(g, r, Height / 2, BackColor);
            Pal.DrawRound(g, r, Height / 2, hover ? Pal.TextSub : Pal.InputBrd, 1f);
        }
    }

    class NeoSwitch : CheckBox
    {
        const int TrackW = 40, TrackH = 22;

        public NeoSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(TrackW + 4, TrackH + 6);
            Cursor = Cursors.Hand;
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Pal.Bg);
            Rectangle track = new Rectangle(2, (Height - TrackH) / 2, TrackW, TrackH);
            Pal.FillRound(g, track, TrackH / 2, Checked ? Pal.Green : Pal.ToggleOff);
            const int kd = 18;
            int kx = Checked ? track.Right - kd - 2 : track.X + 2;
            Rectangle knob = new Rectangle(kx, track.Y + (TrackH - kd) / 2, kd, kd);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                g.FillEllipse(b, knob.X + 1, knob.Y + 2, kd, kd);
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, knob);
        }
    }

    class NeoSlider : Control
    {
        int min = 1, max = 10, val = 10;
        bool dragging;

        public event EventHandler ValueChanged;

        public NeoSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(150, 28);
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return min; } set { min = value; Clamp(); Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; Clamp(); Invalidate(); } }

        public int Value
        {
            get { return val; }
            set
            {
                int v = value;
                if (v < min) v = min;
                if (v > max) v = max;
                if (v == val) return;
                val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        void Clamp()
        {
            if (val < min) val = min;
            if (val > max) val = max;
        }

        int Travel { get { return (int)Math.Round((double)(val - min) / (max - min) * (Width - 18)); } }

        void SetFromX(int x)
        {
            double frac = (x - 9) / (double)(Width - 18);
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            Value = min + (int)Math.Round(frac * (max - min));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            dragging = true; Capture = true; SetFromX(e.X); base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) SetFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false; Capture = false; base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Pal.Bg);
            Rectangle groove = new Rectangle(0, (Height - 16) / 2, Width, 16);
            using (GraphicsPath p = Pal.RoundPath(groove, 8))
            {
                using (SolidBrush b = new SolidBrush(Pal.Groove)) g.FillPath(b, p);
                g.SetClip(new Rectangle(0, 0, 9 + Travel, Height));
                using (SolidBrush b = new SolidBrush(Pal.Sub)) g.FillPath(b, p);
                g.ResetClip();
            }
            int kx = Travel;
            Rectangle knob = new Rectangle(kx, (Height - 18) / 2, 18, 18);
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, knob);
            using (Pen p = new Pen(Pal.Green, 2f)) g.DrawEllipse(p, knob);
        }
    }

    class FlatCombo : ComboBox
    {
        public FlatCombo()
        {
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            BackColor = Pal.Card;
            ForeColor = Pal.Text;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            base.OnDrawItem(e);
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(sel ? Pal.Hover : Pal.Card)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, e.Bounds, Pal.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0xF && IsHandleCreated) // WM_PAINT: repaint the system drop button as flat white + chevron
            {
                using (Graphics g = Graphics.FromHwnd(Handle))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (SolidBrush b = new SolidBrush(Pal.Card)) g.FillRectangle(b, Width - 20, 1, 19, Height - 2);
                    using (Pen p = new Pen(Pal.TextSub, 1.4f))
                    {
                        int cx = Width - 11, cy = Height / 2;
                        g.DrawLines(p, new Point[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 3), new Point(cx + 4, cy - 2) });
                    }
                }
            }
        }
    }

    class SettingsForm : Form
    {
        Color pickZh, pickEn;
        readonly List<Rectangle> fieldBorders = new List<Rectangle>();

        const int FW = 420, FH = 792, MX = 24, CW = 372, PAD = 16, RX = FW - MX - PAD; // RX: control right edge
        const int Card1Y = 52, Card1H = 170;
        const int Card2Y = 236, Card2H = 252;
        const int Card3Y = 502, Card3H = 210;
        const int DivY = 726;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public SettingsForm()
        {
            pickZh = Settings.Zh; pickEn = Settings.En;

            Text = "圆点设置";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(FW, FH);
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Pal.Bg;
            ForeColor = Pal.Text;
            DoubleBuffered = true;
            Region = new Region(Pal.RoundPath(new Rectangle(0, 0, FW, FH), 14));
            MouseDown += OnDragBar;

            Label title = new Label();
            title.Text = "圆点设置"; title.AutoSize = true;
            title.Location = new Point(MX, 14);
            title.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            title.ForeColor = Pal.Text; title.BackColor = Color.Transparent;
            title.MouseDown += OnDragBar;

            FlatBtn close = new FlatBtn(); close.Kind = BtnKind.Ghost;
            close.Text = "✕"; close.Location = new Point(FW - 48, 12); close.Size = new Size(28, 28);
            close.Font = new Font("Microsoft YaHei UI", 9f);
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            // ---- 卡片一：颜色与大小 ----
            Label g1 = GroupTitle("颜色与大小", Card1Y + 12);
            int y = Card1Y + 38;

            Label l1 = RowLabel("中文时颜色", MX + PAD, y + 7);
            SwatchBtn btnZh = new SwatchBtn(); btnZh.BackColor = pickZh;
            btnZh.Location = new Point(RX - 56, y + 4); btnZh.Size = new Size(56, 24);
            btnZh.Click += delegate { Color c = Pick(pickZh); if (!c.IsEmpty) { pickZh = c; btnZh.BackColor = c; } };

            Label l2 = RowLabel("英文时颜色", MX + PAD, y + 49);
            SwatchBtn btnEn = new SwatchBtn(); btnEn.BackColor = pickEn;
            btnEn.Location = new Point(RX - 56, y + 46); btnEn.Size = new Size(56, 24);
            btnEn.Click += delegate { Color c = Pick(pickEn); if (!c.IsEmpty) { pickEn = c; btnEn.BackColor = c; } };

            Label l3 = RowLabel("大小（像素）", MX + PAD, y + 91);
            NumericUpDown numSize = new NumericUpDown();
            numSize.Minimum = 4; numSize.Maximum = 40; numSize.Value = Settings.Size;
            numSize.Location = new Point(RX - 80, y + 90); numSize.Width = 80;
            StyleField(numSize); AddBorder(numSize);

            // ---- 卡片二：位置与运动 ----
            Label g2 = GroupTitle("位置与运动", Card2Y + 12);
            y = Card2Y + 38;

            Label l4 = RowLabel("位置（相对光标）", MX + PAD, y + 7);
            FlatCombo cboPos = new FlatCombo();
            cboPos.Items.AddRange(new object[] { "右下", "左下", "右上", "左上" });
            cboPos.SelectedIndex = Settings.Pos;
            cboPos.Location = new Point(RX - 110, y + 4); cboPos.Width = 110;
            AddBorder(cboPos);

            Label l5 = RowLabel("水平间距", MX + PAD, y + 49);
            NumericUpDown numDx = new NumericUpDown();
            numDx.Minimum = 0; numDx.Maximum = 200; numDx.Value = Settings.Dx;
            numDx.Location = new Point(RX - 80, y + 48); numDx.Width = 80;
            StyleField(numDx); AddBorder(numDx);

            Label l6 = RowLabel("垂直间距", MX + PAD, y + 91);
            NumericUpDown numDy = new NumericUpDown();
            numDy.Minimum = 0; numDy.Maximum = 200; numDy.Value = Settings.Dy;
            numDy.Location = new Point(RX - 80, y + 90); numDy.Width = 80;
            StyleField(numDy); AddBorder(numDy);

            Label l10 = RowLabel("跟随速度（慢1~快10）", MX + PAD, y + 133);
            Label valSpeed = new Label(); valSpeed.AutoSize = true;
            valSpeed.Text = Settings.Speed.ToString(); valSpeed.ForeColor = Pal.Text;
            valSpeed.BackColor = Color.Transparent; valSpeed.Location = new Point(RX - 22, y + 136);
            NeoSlider sldSpeed = new NeoSlider();
            sldSpeed.Minimum = 1; sldSpeed.Maximum = 10; sldSpeed.Value = Settings.Speed;
            sldSpeed.Location = new Point(RX - 32 - 150, y + 130);
            sldSpeed.ValueChanged += delegate { valSpeed.Text = sldSpeed.Value.ToString(); };

            Label l11 = RowLabel("不透明度（%）", MX + PAD, y + 175);
            Label valOpacity = new Label(); valOpacity.AutoSize = true;
            valOpacity.Text = Settings.Opacity.ToString(); valOpacity.ForeColor = Pal.Text;
            valOpacity.BackColor = Color.Transparent; valOpacity.Location = new Point(RX - 22, y + 178);
            NeoSlider sldOpacity = new NeoSlider();
            sldOpacity.Minimum = 20; sldOpacity.Maximum = 100; sldOpacity.Value = Settings.Opacity;
            sldOpacity.Location = new Point(RX - 32 - 150, y + 172);
            sldOpacity.ValueChanged += delegate { valOpacity.Text = sldOpacity.Value.ToString(); };

            // ---- 卡片三：图案设置 ----
            Label g3 = GroupTitle("图案设置", Card3Y + 12);
            y = Card3Y + 38;

            Label l12 = RowLabel("闲置跳舞", MX + PAD, y + 7);
            NeoSwitch chkDance = new NeoSwitch();
            chkDance.Checked = Settings.Dance;
            chkDance.Location = new Point(RX - 44, y + 4);

            Label l7 = RowLabel("中文图案", MX + PAD, y + 49);
            string pendingZh = null, pendingEn = null;
            bool pendingClear = false;
            FlatBtn btnImgZh = new FlatBtn(); btnImgZh.Kind = BtnKind.Soft;
            btnImgZh.Text = System.IO.File.Exists(Settings.ImagePath(true)) ? "已选择(点击更换)" : "选择图片…";
            btnImgZh.Location = new Point(RX - 104, y + 45); btnImgZh.Size = new Size(104, 30);
            btnImgZh.Click += delegate
            {
                string f = PickFile();
                if (f != null) { pendingZh = f; btnImgZh.Text = "已选择(点击更换)"; }
            };

            Label l8 = RowLabel("英文图案", MX + PAD, y + 91);
            FlatBtn btnImgEn = new FlatBtn(); btnImgEn.Kind = BtnKind.Soft;
            btnImgEn.Text = System.IO.File.Exists(Settings.ImagePath(false)) ? "已选择(点击更换)" : "选择图片…";
            btnImgEn.Location = new Point(RX - 104, y + 87); btnImgEn.Size = new Size(104, 30);
            btnImgEn.Click += delegate
            {
                string f = PickFile();
                if (f != null) { pendingEn = f; btnImgEn.Text = "已选择(点击更换)"; }
            };

            Label l9 = RowLabel("图案裁剪形状", MX + PAD, y + 133);
            LinkLabel btnDef = new LinkLabel();
            btnDef.Text = "恢复默认圆点"; btnDef.AutoSize = true;
            btnDef.Location = new Point(RX - 84, y + 136);
            btnDef.LinkColor = Pal.Green; btnDef.ActiveLinkColor = Pal.GreenHover;
            btnDef.LinkBehavior = LinkBehavior.HoverUnderline;
            btnDef.Click += delegate
            {
                // Deletion is deferred to 确定 (and then goes to the recycle bin),
                // so 取消 can never leave the user's custom images destroyed.
                pendingClear = true; pendingZh = null; pendingEn = null;
                btnImgZh.Text = "待恢复默认"; btnImgEn.Text = "待恢复默认";
            };
            FlatCombo cboCrop = new FlatCombo();
            cboCrop.Items.AddRange(new object[] { "圆形", "正方形" });
            cboCrop.SelectedIndex = Settings.Crop;
            cboCrop.Location = new Point(btnDef.Location.X - 8 - 80, y + 130); cboCrop.Width = 80;
            AddBorder(cboCrop);

            // ---- 底部操作栏 ----
            LinkLabel lnk = new LinkLabel();
            lnk.Text = "项目主页与更新下载（GitHub）";
            lnk.Location = new Point(MX, DivY + 20); lnk.AutoSize = true;
            lnk.LinkColor = Pal.Green; lnk.ActiveLinkColor = Pal.GreenHover;
            lnk.LinkBehavior = LinkBehavior.HoverUnderline;
            lnk.LinkClicked += delegate
            {
                try { System.Diagnostics.Process.Start("https://github.com/bigbigee/CursorElf"); }
                catch { }
            };

            FlatBtn cancel = new FlatBtn(); cancel.Kind = BtnKind.Ghost; cancel.Text = "取消";
            cancel.Location = new Point(FW - MX - 88 - 10 - 80, DivY + 14); cancel.Size = new Size(80, 34);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            FlatBtn ok = new FlatBtn(); ok.Kind = BtnKind.Primary; ok.Text = "确定";
            ok.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            ok.Location = new Point(FW - MX - 88, DivY + 14); ok.Size = new Size(88, 34);
            ok.Click += delegate
            {
                bool imgFail = false;
                if (pendingZh != null && !Settings.SetImage(true, pendingZh)) imgFail = true;
                if (pendingEn != null && !Settings.SetImage(false, pendingEn)) imgFail = true;
                if (imgFail)
                {
                    MessageBox.Show("部分图片无法应用（格式不支持或文件被占用），请重新选择。",
                        "山竹 Cursor Elf", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return; // keep the dialog open so the user can fix the choice
                }
                if (pendingClear) Settings.ClearImages();
                Settings.Zh = pickZh; Settings.En = pickEn;
                Settings.Size = (int)numSize.Value; Settings.Pos = cboPos.SelectedIndex;
                Settings.Dx = (int)numDx.Value; Settings.Dy = (int)numDy.Value;
                Settings.Speed = sldSpeed.Value;
                Settings.Opacity = sldOpacity.Value;
                Settings.Dance = chkDance.Checked;
                Settings.Crop = cboCrop.SelectedIndex;
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(title); Controls.Add(close);
            Controls.Add(g1); Controls.Add(l1); Controls.Add(btnZh);
            Controls.Add(l2); Controls.Add(btnEn);
            Controls.Add(l3); Controls.Add(numSize);
            Controls.Add(g2); Controls.Add(l4); Controls.Add(cboPos);
            Controls.Add(l5); Controls.Add(numDx);
            Controls.Add(l6); Controls.Add(numDy);
            Controls.Add(l10); Controls.Add(sldSpeed); Controls.Add(valSpeed);
            Controls.Add(l11); Controls.Add(sldOpacity); Controls.Add(valOpacity);
            Controls.Add(g3); Controls.Add(l12); Controls.Add(chkDance);
            Controls.Add(l7); Controls.Add(btnImgZh);
            Controls.Add(l8); Controls.Add(btnImgEn);
            Controls.Add(l9); Controls.Add(cboCrop); Controls.Add(btnDef);
            Controls.Add(lnk);
            Controls.Add(cancel); Controls.Add(ok);
            AcceptButton = ok; CancelButton = cancel;
        }

        void OnDragBar(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN / HTCAPTION
        }

        Label GroupTitle(string text, int ty)
        {
            Label l = new Label();
            l.Text = text; l.AutoSize = true; l.Location = new Point(MX + PAD, ty);
            l.ForeColor = Pal.Title; l.BackColor = Color.Transparent;
            l.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            return l;
        }

        Label RowLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text; l.AutoSize = true; l.Location = new Point(x, y);
            l.ForeColor = Pal.TextSub; l.BackColor = Color.Transparent;
            return l;
        }

        void AddBorder(Control c)
        {
            fieldBorders.Add(new Rectangle(c.Location.X - 4, c.Location.Y - 2, c.Width + 8, c.Height + 4));
        }

        static void StyleField(NumericUpDown n)
        {
            n.BackColor = Pal.Card; n.ForeColor = Pal.Text;
            n.BorderStyle = BorderStyle.None;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawCard(g, Card1Y, Card1H);
            DrawCard(g, Card2Y, Card2H);
            DrawCard(g, Card3Y, Card3H);
            using (Pen p = new Pen(Pal.Divider)) g.DrawLine(p, MX, DivY, FW - MX, DivY);
            using (Pen p = new Pen(Pal.InputBrd))
                foreach (Rectangle r in fieldBorders)
                    using (GraphicsPath pth = Pal.RoundPath(r, 8)) g.DrawPath(p, pth);
            Pal.DrawRound(g, new Rectangle(0, 0, FW - 1, FH - 1), 14, Pal.Border, 1f);
        }

        static void DrawCard(Graphics g, int cy, int ch)
        {
            Rectangle r = new Rectangle(MX, cy, CW, ch);
            Pal.FillRound(g, r, 12, Pal.Card);
            Pal.DrawRound(g, r, 12, Pal.Divider, 1f);
        }

        static Color Pick(Color current)
        {
            using (ColorDialog cd = new ColorDialog())
            {
                cd.FullOpen = true;
                cd.Color = current;
                return cd.ShowDialog() == DialogResult.OK ? cd.Color : Color.Empty;
            }
        }

        static string PickFile()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
                ofd.Title = "选择图案图片";
                return ofd.ShowDialog() == DialogResult.OK ? ofd.FileName : null;
            }
        }
    }

    class AppContext : ApplicationContext
    {
        public static AppContext Current;
        static int balloonShown;

        readonly OverlayForm overlay;
        readonly System.Windows.Forms.Timer timer;
        readonly NotifyIcon tray;
        readonly Icon trayIcon;
        bool? lastChinese = null;
        int tickCount = 0;

        IntPtr mouseHook;
        Native.LowLevelMouseProc mouseProc;
        bool mouseSeenLogged = false;
        bool pollShiftDown, pollAlone;
        int pollDownTick;
        readonly bool[] lastDown = new bool[256];
        Rectangle lastBounds = Rectangle.Empty;

        int targetX, targetY;

        // Idle dance: after 60 seconds of wall-clock without any mouse OR keyboard
        // activity the dot orbits its anchor until activity resumes.
        // (Tick counting is unsafe here: WM_TIMER coalesces and drifts ~1.3x under load.)
        const int IDLE_MS = 60000;
        const int WATCHDOG_MS = 10000;
        int lastActivityTick;
        int lastMouseEvtTick;
        bool dancing;
        double dancePhase;
        int anchorX, anchorY;
        Native.POINT lastCursor;

        const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string APP_NAME = "ImeCursorDot";

        public AppContext()
        {
            Current = this;
            overlay = new OverlayForm();
            trayIcon = LoadAppIcon();

            ContextMenuStrip menu = BuildMenu(this);

            tray = new NotifyIcon();
            tray.Icon = trayIcon;
            tray.ContextMenuStrip = menu;
            tray.Text = "山竹 Cursor Elf  绿=中文  红=英文";
            tray.Visible = true;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 25;
            timer.Tick += Tick;
            lastActivityTick = Environment.TickCount;
            lastMouseEvtTick = Environment.TickCount;
            timer.Start();
            overlay.FormClosed += delegate
            {
                UninstallHooks();
                Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPower;
                tray.Visible = false; tray.Dispose(); ExitThread();
            };
            InstallHooks();
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPower;
            overlay.Show();
        }

        static Icon LoadAppIcon()
        {
            try
            {
                Icon ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ico != null) return ico;
            }
            catch (Exception) { }
            return Native.MakeIcon(16, Settings.Zh);
        }

        internal static ContextMenuStrip BuildMenu(AppContext ctx)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new CardMenuRenderer();
            menu.Font = new Font("Microsoft YaHei UI", 9.5f);
            menu.ImageScalingSize = new Size(16, 16);
            menu.Padding = new Padding(4, 6, 4, 6);
            menu.Layout += delegate
            {
                Region old = menu.Region;
                menu.Region = new Region(Pal.RoundPath(new Rectangle(0, 0, menu.Width, menu.Height), 10));
                if (old != null) old.Dispose();
            };

            ToolStripMenuItem autostart = new ToolStripMenuItem("开机自启动");
            autostart.Image = MenuIcons.Check(ctx != null && AutoStartEnabled());
            autostart.Click += delegate
            {
                if (ctx == null) return;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (k == null) return;
                    if (k.GetValue(APP_NAME) == null)
                        k.SetValue(APP_NAME, "\"" + Application.ExecutablePath + "\"");
                    else
                        k.DeleteValue(APP_NAME, false);
                    bool on = k.GetValue(APP_NAME) != null;
                    autostart.Checked = on;
                    autostart.Image = MenuIcons.Check(on);
                }
            };
            ToolStripMenuItem header = new ToolStripMenuItem("校正");
            header.Enabled = false;
            ToolStripMenuItem markZh = new ToolStripMenuItem("当前为中文");
            markZh.Image = MenuIcons.Dot(Settings.Zh);
            markZh.Click += delegate { if (ctx != null) { Native.MirrorChinese = true; Native.MirrorDirty = true; } };
            ToolStripMenuItem markEn = new ToolStripMenuItem("当前为英文");
            markEn.Image = MenuIcons.Dot(Settings.En);
            markEn.Click += delegate { if (ctx != null) { Native.MirrorChinese = false; Native.MirrorDirty = true; } };
            ToolStripMenuItem settings = new ToolStripMenuItem("设置");
            settings.Image = MenuIcons.Glyph("\uE713");
            settings.Click += delegate
            {
                if (ctx == null) return;
                using (SettingsForm sf = new SettingsForm())
                {
                    if (sf.ShowDialog() == DialogResult.OK) ctx.ApplySettings();
                }
            };
            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Image = MenuIcons.Glyph("\uE7E8");
            exit.Click += delegate { Application.Exit(); };
            foreach (ToolStripMenuItem it in new ToolStripMenuItem[] { autostart, header, markZh, markEn, settings, exit })
                it.Padding = new Padding(6, 4, 10, 4);
            menu.Items.AddRange(new ToolStripItem[] {
                autostart, new ToolStripSeparator(),
                header, markZh, markEn, new ToolStripSeparator(),
                settings, new ToolStripSeparator(), exit });
            return menu;
        }

        void ApplySettings()
        {
            dancing = false;
            lastActivityTick = Environment.TickCount;
            overlay.ClientSize = new Size(Settings.Size, Settings.Size);
            lastChinese = null;
            Native.POINT c;
            if (Native.GetCursorPos(out c)) MoveDot(c.X, c.Y);
        }

        public void OnThreadException(Exception ex)
        {
            Crash(ex);
        }

        static void Crash(object exObj)
        {
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\ImeCursorDot";
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(dir + "\\crash.log",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " +
                    (exObj == null ? "null" : exObj.ToString()) + "\r\n");
                if (Current != null && Current.tray.Visible &&
                    System.Threading.Interlocked.CompareExchange(ref balloonShown, 1, 0) == 0)
                    Current.tray.ShowBalloonTip(8000, "山竹 Cursor Elf",
                        "发生内部错误，程序会继续运行，详情见 crash.log。",
                        ToolTipIcon.Warning);
            }
            catch (Exception) { }
        }

        void InstallHooks()
        {
            UninstallHooks();
            if (mouseProc == null) mouseProc = OnMouse;
            mouseHook = Native.SetWindowsHookEx(14 /*WH_MOUSE_LL*/, mouseProc, Native.GetModuleHandle(null), 0);
            lastMouseEvtTick = Environment.TickCount;
            Dbg("install mouse=" + mouseHook + " err=" + Marshal.GetLastWin32Error());
            pollShiftDown = pollAlone = false;
        }

        static object dbgLock = new object();
        static void Dbg(string s)
        {
            if (!DebugLog.Enabled) return;
            try
            {
                lock (dbgLock)
                {
                    System.IO.File.AppendAllText(DebugLog.File,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\n");
                }
            }
            catch (Exception) { }
        }

        void UninstallHooks()
        {
            if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        }

        void OnPower(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            // Idle/sleep suspends this thread; Windows then removes the low-level hooks.
            InstallHooks();
            Native.MirrorDirty = true;
            lastChinese = null;
        }

        int OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam.ToInt64() == 0x200 /* WM_MOUSEMOVE */)
            {
                lastMouseEvtTick = Environment.TickCount;
                if (!mouseSeenLogged) { mouseSeenLogged = true; Dbg("mousemove hook ACTIVE"); }
                // Hook lParam is raw physical pixels; with mixed-DPI multi-monitor it
                // diverges from desktop coordinates on the secondary screen. GetCursorPos
                // returns coordinates in the same space as the overlay window position.
                Native.POINT c;
                if (Native.GetCursorPos(out c) && (c.X != lastCursor.X || c.Y != lastCursor.Y))
                {
                    lastCursor = c;
                    CursorMoved(c.X, c.Y);
                }
            }
            return (int)Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        void MoveDot(int cx, int cy)
        {
            int s = Settings.Size;
            if (cx < lastBounds.Left || cx >= lastBounds.Right ||
                cy < lastBounds.Top || cy >= lastBounds.Bottom)
                lastBounds = Screen.FromPoint(new Point(cx, cy)).Bounds;
            int x = (Settings.Pos == 1 || Settings.Pos == 3) ? cx - Settings.Dx - s : cx + Settings.Dx;
            int y = (Settings.Pos >= 2) ? cy - Settings.Dy - s : cy + Settings.Dy;
            x = Math.Min(Math.Max(x, lastBounds.Left), lastBounds.Right - s - 2);
            y = Math.Min(Math.Max(y, lastBounds.Top), lastBounds.Bottom - s - 2);
            targetX = x; targetY = y;
            if (Settings.Speed >= 10 && (overlay.Left != x || overlay.Top != y))
                overlay.Location = new Point(x, y);
        }

        // Linear easing toward the cursor each tick; Speed 10 means "snap instantly".
        void FollowStep()
        {
            if (Settings.Speed >= 10) return;
            int x = overlay.Left, y = overlay.Top;
            if (x == targetX && y == targetY) return;
            double f = Math.Min(1.0, Settings.Speed / 2.0);
            int nx = x + (int)Math.Round((targetX - x) * f);
            int ny = y + (int)Math.Round((targetY - y) * f);
            if (Math.Abs(targetX - nx) <= 1) nx = targetX;
            if (Math.Abs(targetY - ny) <= 1) ny = targetY;
            overlay.Location = new Point(nx, ny);
        }

        static bool AutoStartEnabled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY))
                return k != null && k.GetValue(APP_NAME) != null;
        }

        static bool Down(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

        // Shift-tap detection by polling the system's real-time key state every tick.
        // No keyboard hook involved, so security software and power management cannot break it.
        void DetectShiftTap()
        {
            bool shift = Down(0x10);

            // Handle the Shift down-edge BEFORE scanning other keys: when Shift and
            // another key go down inside the same poll window, the old order let the
            // other key's edge slip past the cancel check and toggle wrongly.
            if (shift && !pollShiftDown)
            {
                pollShiftDown = true;
                pollDownTick = Environment.TickCount;
                // Modifiers already held BEFORE the Shift press don't cancel the tap
                // (same semantics as v1.3; shift L/R excluded as they ARE the shift).
                pollAlone = !(Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C)
                    || Down(0xA2) || Down(0xA3) || Down(0xA4) || Down(0xA5));
            }

            // Any key going down while Shift is held means the user is typing
            // (Shift+punctuation, Shift+letter...), which Microsoft Pinyin does NOT
            // count as a mode switch — cancel the tap. Any key press also counts as
            // user activity so the idle dance never starts while typing.
            for (int vk = 1; vk < 256; vk++)
            {
                bool d = vk == 0x10 ? shift : Down(vk);
                if (d && !lastDown[vk])
                {
                    lastActivityTick = Environment.TickCount;
                    if (pollShiftDown && vk != 0x10 && vk != 0xA0 && vk != 0xA1)
                        pollAlone = false;
                }
                lastDown[vk] = d;
            }

            if (!shift && pollShiftDown)
            {
                pollShiftDown = false;
                int dur = unchecked(Environment.TickCount - pollDownTick);
                if (pollAlone && dur < 900)
                {
                    Native.MirrorChinese = !Native.MirrorChinese;
                    Native.MirrorDirty = true;
                    Dbg("poll TOGGLE dur=" + dur + " -> mirror=" + Native.MirrorChinese);
                }
            }
        }

        void CursorMoved(int x, int y)
        {
            lastActivityTick = Environment.TickCount;
            dancing = false;
            MoveDot(x, y);
        }

        void DanceStep()
        {
            dancePhase += 0.11;
            int s = Settings.Size;
            int r = s + 10 + (int)Math.Round(4 * Math.Sin(dancePhase * 1.7));
            int x = anchorX + (int)Math.Round(r * Math.Cos(dancePhase));
            int y = anchorY + (int)Math.Round(r * 0.55 * Math.Sin(dancePhase * 2));
            x = Math.Min(Math.Max(x, lastBounds.Left), lastBounds.Right - s - 2);
            y = Math.Min(Math.Max(y, lastBounds.Top), lastBounds.Bottom - s - 2);
            overlay.Location = new Point(x, y);
        }

        void Tick(object sender, EventArgs e)
        {
            DetectShiftTap();

            Native.POINT c;
            if (Native.GetCursorPos(out c) && (c.X != lastCursor.X || c.Y != lastCursor.Y))
            {
                lastCursor = c;
                CursorMoved(c.X, c.Y);
            }

            if (dancing)
            {
                DanceStep();
            }
            else
            {
                if (Settings.Dance && unchecked(Environment.TickCount - lastActivityTick) >= IDLE_MS)
                {
                    dancing = true;
                    anchorX = targetX; anchorY = targetY;
                    dancePhase = 0;
                }
                else FollowStep();
            }

            // Keep the dot above other windows: re-assert topmost z-order every tick.
            Native.SetWindowPos(overlay.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOREDRAW);

            if (lastChinese == null || Native.MirrorDirty || (tickCount & 3) == 0)
            {
                Native.MirrorDirty = false;
                bool zh = Native.IsChineseMode();
                if (lastChinese == null || lastChinese.Value != zh)
                {
                    Native.PaintDot(overlay.Handle, Settings.Size, zh);
                    lastChinese = zh;
                }
            }

            // Watchdog: re-arm the mouse hook only when it has been silent past the
            // grace period — unconditional periodic reinstalls themselves opened an
            // event-loss window roughly every 13 seconds during active use.
            tickCount++;
            if (!pollShiftDown && unchecked(Environment.TickCount - lastMouseEvtTick) >= WATCHDOG_MS + 2000)
            {
                lastMouseEvtTick = Environment.TickCount;
                InstallHooks();
                Dbg("watchdog re-armed mouse hook");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); }
            base.Dispose(disposing);
        }
    }

    static class DebugLog
    {
        public static bool Enabled;
        public static string File
        {
            get
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                    + "\\ImeCursorDot\\debug.log";
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (a == "--debug") DebugLog.Enabled = true;
            Application.ThreadException += delegate(object src, System.Threading.ThreadExceptionEventArgs e)
            {
                if (AppContext.Current != null) AppContext.Current.OnThreadException(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object src, UnhandledExceptionEventArgs e)
            {
                if (AppContext.Current != null) AppContext.Current.OnThreadException(e.ExceptionObject as Exception);
            };
            bool createdNew;
            using (Mutex mtx = new Mutex(true, "ImeCursorDot_SingleInstance", out createdNew))
            {
                if (!createdNew) return;
                Settings.Load();
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.Run(new AppContext());
            }
        }
    }
}

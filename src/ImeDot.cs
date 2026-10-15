using System;
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

    class MangosteenRenderer : ToolStripProfessionalRenderer
    {
        static readonly Color Rind = Color.FromArgb(109, 47, 63);    // 山竹果皮深紫
        static readonly Color Flesh = Color.FromArgb(250, 243, 231); // 果肉米白
        static readonly Color Blush = Color.FromArgb(240, 223, 212); // 选中淡粉
        static readonly Color Ink = Color.FromArgb(74, 35, 51);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Flesh)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Rind)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            Rectangle r = new Rectangle(1, 0, e.Item.Width - 2, e.Item.Height - 1);
            using (SolidBrush b = new SolidBrush(Blush)) e.Graphics.FillRectangle(b, r);
            using (SolidBrush b = new SolidBrush(Rind)) e.Graphics.FillRectangle(b, new Rectangle(2, r.Top + 3, 3, r.Height - 6));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Rind : (e.Item.Enabled ? Ink : Color.Gray);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Color.FromArgb(130, Rind))) e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Flesh)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }
    }

    class SettingsForm : Form
    {
        Color pickZh, pickEn;
        int pickSize, pickPos;

        public SettingsForm()
        {
            pickZh = Settings.Zh; pickEn = Settings.En;
            pickSize = Settings.Size; pickPos = Settings.Pos;

            Text = "圆点设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(310, 498);
            Font = new Font("Microsoft YaHei", 9f);

            Label l1 = new Label(); l1.Text = "中文时颜色"; l1.Location = new Point(16, 16); l1.AutoSize = true;
            Button btnZh = new Button(); btnZh.Location = new Point(140, 12); btnZh.Size = new Size(80, 26);
            btnZh.BackColor = pickZh;
            btnZh.Click += delegate { Color c = Pick(pickZh); if (!c.IsEmpty) { pickZh = c; btnZh.BackColor = c; } };

            Label l2 = new Label(); l2.Text = "英文时颜色"; l2.Location = new Point(16, 50); l2.AutoSize = true;
            Button btnEn = new Button(); btnEn.Location = new Point(140, 46); btnEn.Size = new Size(80, 26);
            btnEn.BackColor = pickEn;
            btnEn.Click += delegate { Color c = Pick(pickEn); if (!c.IsEmpty) { pickEn = c; btnEn.BackColor = c; } };

            Label l3 = new Label(); l3.Text = "大小(像素)"; l3.Location = new Point(16, 86); l3.AutoSize = true;
            NumericUpDown numSize = new NumericUpDown();
            numSize.Location = new Point(140, 82); numSize.Width = 70;
            numSize.Minimum = 4; numSize.Maximum = 40; numSize.Value = pickSize;

            Label l4 = new Label(); l4.Text = "位置(相对光标)"; l4.Location = new Point(16, 120); l4.AutoSize = true;
            ComboBox cboPos = new ComboBox();
            cboPos.Location = new Point(140, 116); cboPos.Width = 110;
            cboPos.DropDownStyle = ComboBoxStyle.DropDownList;
            cboPos.Items.AddRange(new object[] { "右下", "左下", "右上", "左上" });
            cboPos.SelectedIndex = pickPos;

            Label l5 = new Label(); l5.Text = "水平间距"; l5.Location = new Point(16, 154); l5.AutoSize = true;
            NumericUpDown numDx = new NumericUpDown();
            numDx.Location = new Point(140, 150); numDx.Width = 60;
            numDx.Minimum = 0; numDx.Maximum = 200; numDx.Value = Settings.Dx;

            Label l6 = new Label(); l6.Text = "垂直间距"; l6.Location = new Point(16, 186); l6.AutoSize = true;
            NumericUpDown numDy = new NumericUpDown();
            numDy.Location = new Point(140, 182); numDy.Width = 60;
            numDy.Minimum = 0; numDy.Maximum = 200; numDy.Value = Settings.Dy;

            Label l10 = new Label(); l10.Text = "跟随速度(慢1~快10)"; l10.Location = new Point(16, 220); l10.AutoSize = true;
            NumericUpDown numSpeed = new NumericUpDown();
            numSpeed.Location = new Point(140, 216); numSpeed.Width = 60;
            numSpeed.Minimum = 1; numSpeed.Maximum = 10; numSpeed.Value = Settings.Speed;

            Label l11 = new Label(); l11.Text = "不透明度(%)"; l11.Location = new Point(16, 254); l11.AutoSize = true;
            NumericUpDown numOpacity = new NumericUpDown();
            numOpacity.Location = new Point(140, 250); numOpacity.Width = 60;
            numOpacity.Minimum = 20; numOpacity.Maximum = 100; numOpacity.Value = Settings.Opacity;

            CheckBox chkDance = new CheckBox(); chkDance.Text = "闲置 1 分钟时圆点跳舞"; chkDance.Location = new Point(140, 286); chkDance.AutoSize = true;
            chkDance.Checked = Settings.Dance;

            Label l7 = new Label(); l7.Text = "中文图案"; l7.Location = new Point(16, 322); l7.AutoSize = true;
            Button btnImgZh = new Button(); btnImgZh.Location = new Point(140, 318); btnImgZh.Size = new Size(120, 26);
            btnImgZh.Text = System.IO.File.Exists(Settings.ImagePath(true)) ? "已选择(点击更换)" : "选择图片…";
            string pendingZh = null, pendingEn = null;
            bool pendingClear = false;
            btnImgZh.Click += delegate
            {
                string f = PickFile();
                if (f != null) { pendingZh = f; btnImgZh.Text = "已选择(点击更换)"; }
            };

            Label l8 = new Label(); l8.Text = "英文图案"; l8.Location = new Point(16, 356); l8.AutoSize = true;
            Button btnImgEn = new Button(); btnImgEn.Location = new Point(140, 352); btnImgEn.Size = new Size(120, 26);
            btnImgEn.Text = System.IO.File.Exists(Settings.ImagePath(false)) ? "已选择(点击更换)" : "选择图片…";
            btnImgEn.Click += delegate
            {
                string f = PickFile();
                if (f != null) { pendingEn = f; btnImgEn.Text = "已选择(点击更换)"; }
            };

            Label l9 = new Label(); l9.Text = "图案裁剪形状"; l9.Location = new Point(16, 392); l9.AutoSize = true;
            ComboBox cboCrop = new ComboBox();
            cboCrop.Location = new Point(140, 388); cboCrop.Width = 80;
            cboCrop.DropDownStyle = ComboBoxStyle.DropDownList;
            cboCrop.Items.AddRange(new object[] { "圆形", "正方形" });
            cboCrop.SelectedIndex = Settings.Crop;

            Button btnDef = new Button(); btnDef.Text = "恢复默认圆点"; btnDef.Location = new Point(230, 386); btnDef.Size = new Size(72, 30);
            btnDef.Click += delegate
            {
                // Deletion is deferred to 确定 (and then goes to the recycle bin),
                // so 取消 can never leave the user's custom images destroyed.
                pendingClear = true; pendingZh = null; pendingEn = null;
                btnImgZh.Text = "待恢复默认"; btnImgEn.Text = "待恢复默认";
            };

            LinkLabel lnk = new LinkLabel();
            lnk.Text = "项目主页与更新下载（GitHub）";
            lnk.Location = new Point(16, 428); lnk.AutoSize = true;
            lnk.LinkClicked += delegate
            {
                try { System.Diagnostics.Process.Start("https://github.com/bigbigee/CursorElf"); }
                catch { }
            };

            Button ok = new Button(); ok.Text = "确定"; ok.Location = new Point(60, 460); ok.Size = new Size(80, 28);
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
                Settings.Speed = (int)numSpeed.Value;
                Settings.Opacity = (int)numOpacity.Value;
                Settings.Dance = chkDance.Checked;
                Settings.Crop = cboCrop.SelectedIndex;
                DialogResult = DialogResult.OK;
                Close();
            };
            Button cancel = new Button(); cancel.Text = "取消"; cancel.Location = new Point(180, 460); cancel.Size = new Size(80, 28);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(l1); Controls.Add(btnZh);
            Controls.Add(l2); Controls.Add(btnEn);
            Controls.Add(l3); Controls.Add(numSize);
            Controls.Add(l4); Controls.Add(cboPos);
            Controls.Add(l5); Controls.Add(numDx);
            Controls.Add(l6); Controls.Add(numDy);
            Controls.Add(l10); Controls.Add(numSpeed);
            Controls.Add(l11); Controls.Add(numOpacity);
            Controls.Add(chkDance);
            Controls.Add(l7); Controls.Add(btnImgZh);
            Controls.Add(l8); Controls.Add(btnImgEn);
            Controls.Add(l9); Controls.Add(cboCrop); Controls.Add(btnDef);
            Controls.Add(lnk);
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
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

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new MangosteenRenderer();
            menu.Font = new Font("Microsoft YaHei UI", 9.5f);

            ToolStripMenuItem autostart = new ToolStripMenuItem("开机自动启动");
            autostart.Click += delegate
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (k == null) return;
                    if (k.GetValue(APP_NAME) == null)
                        k.SetValue(APP_NAME, "\"" + Application.ExecutablePath + "\"");
                    else
                        k.DeleteValue(APP_NAME, false);
                    autostart.Checked = k.GetValue(APP_NAME) != null;
                }
            };
            ToolStripMenuItem markZh = new ToolStripMenuItem("当前实为中文（校正颜色）");
            markZh.Click += delegate { Native.MirrorChinese = true; Native.MirrorDirty = true; };
            ToolStripMenuItem markEn = new ToolStripMenuItem("当前实为英文（校正颜色）");
            markEn.Click += delegate { Native.MirrorChinese = false; Native.MirrorDirty = true; };
            ToolStripMenuItem settings = new ToolStripMenuItem("设置（颜色·大小·速度·外观）…");
            settings.Click += delegate
            {
                using (SettingsForm sf = new SettingsForm())
                {
                    if (sf.ShowDialog() == DialogResult.OK) ApplySettings();
                }
            };
            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { Application.Exit(); };
            menu.Items.AddRange(new ToolStripItem[] {
                autostart, new ToolStripSeparator(),
                markZh, markEn, new ToolStripSeparator(),
                settings, new ToolStripSeparator(), exit });

            tray = new NotifyIcon();
            tray.Icon = trayIcon;
            tray.ContextMenuStrip = menu;
            tray.Text = "山竹 Cursor Elf  绿=中文  红=英文";
            tray.Visible = true;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 25;
            timer.Tick += Tick;
            autostart.Checked = AutoStartEnabled();
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

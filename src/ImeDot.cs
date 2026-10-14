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

        /// <summary>Render a premultiplied-BGRA circle and push it to a layered window.</summary>
        public static void PaintDot(IntPtr hwnd, int size, Color fill)
        {
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    RectangleF rect = new RectangleF(1.5f, 1.5f, size - 3f, size - 3f);
                    using (SolidBrush b = new SolidBrush(fill)) g.FillEllipse(b, rect);
                    using (Pen p = new Pen(Color.FromArgb(230, 0, 0, 0), 1f)) g.DrawEllipse(p, rect);
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
                    SourceConstantAlpha = 235,
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
                return Icon.FromHandle(bmp.GetHicon());
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
                System.IO.File.WriteAllText(file,
                    "zh=" + Zh.ToArgb().ToString("X8") + "\r\n" +
                    "en=" + En.ToArgb().ToString("X8") + "\r\n" +
                    "size=" + Size + "\r\n" +
                    "pos=" + Pos + "\r\n" +
                    "dx=" + Dx + "\r\n" +
                    "dy=" + Dy + "\r\n");
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
            ClientSize = new Size(300, 248);
            Font = new Font("Microsoft YaHei", 9f);

            Label l1 = new Label(); l1.Text = "中文时颜色"; l1.Location = new Point(16, 16); l1.AutoSize = true;
            Button btnZh = new Button(); btnZh.Location = new Point(130, 12); btnZh.Size = new Size(80, 26);
            btnZh.BackColor = pickZh;
            btnZh.Click += delegate { Color c = Pick(pickZh); if (!c.IsEmpty) { pickZh = c; btnZh.BackColor = c; } };

            Label l2 = new Label(); l2.Text = "英文时颜色"; l2.Location = new Point(16, 50); l2.AutoSize = true;
            Button btnEn = new Button(); btnEn.Location = new Point(130, 46); btnEn.Size = new Size(80, 26);
            btnEn.BackColor = pickEn;
            btnEn.Click += delegate { Color c = Pick(pickEn); if (!c.IsEmpty) { pickEn = c; btnEn.BackColor = c; } };

            Label l3 = new Label(); l3.Text = "大小(像素)"; l3.Location = new Point(16, 86); l3.AutoSize = true;
            NumericUpDown numSize = new NumericUpDown();
            numSize.Location = new Point(130, 82); numSize.Width = 70;
            numSize.Minimum = 4; numSize.Maximum = 40; numSize.Value = pickSize;

            Label l4 = new Label(); l4.Text = "位置(相对光标)"; l4.Location = new Point(16, 120); l4.AutoSize = true;
            ComboBox cboPos = new ComboBox();
            cboPos.Location = new Point(130, 116); cboPos.Width = 110;
            cboPos.DropDownStyle = ComboBoxStyle.DropDownList;
            cboPos.Items.AddRange(new object[] { "右下", "左下", "右上", "左上" });
            cboPos.SelectedIndex = pickPos;

            Label l5 = new Label(); l5.Text = "水平间距"; l5.Location = new Point(16, 154); l5.AutoSize = true;
            NumericUpDown numDx = new NumericUpDown();
            numDx.Location = new Point(130, 150); numDx.Width = 60;
            numDx.Minimum = 0; numDx.Maximum = 200; numDx.Value = Settings.Dx;

            Label l6 = new Label(); l6.Text = "垂直间距"; l6.Location = new Point(16, 186); l6.AutoSize = true;
            NumericUpDown numDy = new NumericUpDown();
            numDy.Location = new Point(130, 182); numDy.Width = 60;
            numDy.Minimum = 0; numDy.Maximum = 200; numDy.Value = Settings.Dy;

            Button ok = new Button(); ok.Text = "确定"; ok.Location = new Point(50, 214); ok.Size = new Size(80, 28);
            ok.Click += delegate
            {
                Settings.Zh = pickZh; Settings.En = pickEn;
                Settings.Size = (int)numSize.Value; Settings.Pos = cboPos.SelectedIndex;
                Settings.Dx = (int)numDx.Value; Settings.Dy = (int)numDy.Value;
                Settings.Save();
                DialogResult = DialogResult.OK;
                Close();
            };
            Button cancel = new Button(); cancel.Text = "取消"; cancel.Location = new Point(170, 214); cancel.Size = new Size(80, 28);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(l1); Controls.Add(btnZh);
            Controls.Add(l2); Controls.Add(btnEn);
            Controls.Add(l3); Controls.Add(numSize);
            Controls.Add(l4); Controls.Add(cboPos);
            Controls.Add(l5); Controls.Add(numDx);
            Controls.Add(l6); Controls.Add(numDy);
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
    }

    class AppContext : ApplicationContext
    {
        readonly OverlayForm overlay;
        readonly System.Windows.Forms.Timer timer;
        readonly NotifyIcon tray;
        Icon iconZh;
        Icon iconEn;
        bool? lastChinese = null;
        int tickCount = 0;

        IntPtr mouseHook;
        Native.LowLevelMouseProc mouseProc;
        bool mouseSeenLogged = false;
        bool pollShiftDown, pollAlone;
        int pollDownTick;
        readonly bool[] lastDown = new bool[256];
        Rectangle lastBounds = Rectangle.Empty;

        const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string APP_NAME = "ImeCursorDot";

        public AppContext()
        {
            overlay = new OverlayForm();
            iconZh = Native.MakeIcon(16, Settings.Zh);
            iconEn = Native.MakeIcon(16, Settings.En);

            MenuItem autostart = new MenuItem("开机自动启动");
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
            MenuItem exit = new MenuItem("退出");
            exit.Click += delegate { Application.Exit(); };
            MenuItem markZh = new MenuItem("当前实为中文（校正颜色）");
            markZh.Click += delegate { Native.MirrorChinese = true; Native.MirrorDirty = true; };
            MenuItem markEn = new MenuItem("当前实为英文（校正颜色）");
            markEn.Click += delegate { Native.MirrorChinese = false; Native.MirrorDirty = true; };
            MenuItem settings = new MenuItem("设置（颜色·大小·位置）…");
            settings.Click += delegate
            {
                using (SettingsForm sf = new SettingsForm())
                {
                    if (sf.ShowDialog() == DialogResult.OK) ApplySettings();
                }
            };
            tray = new NotifyIcon();
            tray.Icon = iconZh;
            tray.ContextMenu = new ContextMenu(new MenuItem[] { autostart, new MenuItem("-"), markZh, markEn, new MenuItem("-"), settings, new MenuItem("-"), exit });
            tray.Text = "输入法指示点  绿=中文  红=英文";
            tray.Visible = true;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 25;
            timer.Tick += Tick;
            autostart.Checked = AutoStartEnabled();
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

        void ApplySettings()
        {
            overlay.ClientSize = new Size(Settings.Size, Settings.Size);
            Icon oldZh = iconZh, oldEn = iconEn;
            iconZh = Native.MakeIcon(16, Settings.Zh);
            iconEn = Native.MakeIcon(16, Settings.En);
            tray.Icon = lastChinese == false ? iconEn : iconZh;
            oldZh.Dispose(); oldEn.Dispose();
            lastChinese = null;
            Native.POINT c;
            if (Native.GetCursorPos(out c)) MoveDot(c.X, c.Y);
        }

        void InstallHooks()
        {
            UninstallHooks();
            if (mouseProc == null) mouseProc = OnMouse;
            mouseHook = Native.SetWindowsHookEx(14 /*WH_MOUSE_LL*/, mouseProc, Native.GetModuleHandle(null), 0);
            Dbg("install mouse=" + mouseHook + " err=" + Marshal.GetLastWin32Error());
            pollShiftDown = pollAlone = false;
        }

        static object dbgLock = new object();
        static void Dbg(string s)
        {
            try
            {
                lock (dbgLock)
                {
                    System.IO.File.AppendAllText(
                        AppDomain.CurrentDomain.BaseDirectory + "debug.log",
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
                if (!mouseSeenLogged) { mouseSeenLogged = true; Dbg("mousemove hook ACTIVE"); }
                // Hook lParam is raw physical pixels; with mixed-DPI multi-monitor it
                // diverges from desktop coordinates on the secondary screen. GetCursorPos
                // returns coordinates in the same space as the overlay window position.
                Native.POINT c;
                if (Native.GetCursorPos(out c)) MoveDot(c.X, c.Y);
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
            if (overlay.Left != x || overlay.Top != y) overlay.Location = new Point(x, y);
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
            // Any non-Shift key going down while Shift is held means the user is typing
            // (Shift+punctuation, Shift+letter...), which Microsoft Pinyin does NOT count
            // as a mode switch — cancel the tap.
            for (int vk = 1; vk < 256; vk++)
            {
                bool d = vk == 0x10 ? shift : Down(vk);
                if (d && !lastDown[vk] && vk != 0x10 && pollShiftDown)
                    pollAlone = false;
                lastDown[vk] = d;
            }

            if (shift && !pollShiftDown)
            {
                pollShiftDown = true;
                pollDownTick = Environment.TickCount;
                pollAlone = !(Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C));
            }
            else if (!shift && pollShiftDown)
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

        void Tick(object sender, EventArgs e)
        {
            DetectShiftTap();

            Native.POINT c;
            if (Native.GetCursorPos(out c)) MoveDot(c.X, c.Y);

            if (lastChinese == null || Native.MirrorDirty || (tickCount & 3) == 0)
            {
                Native.MirrorDirty = false;
                bool zh = Native.IsChineseMode();
                if (lastChinese == null || lastChinese.Value != zh)
                {
                    Native.PaintDot(overlay.Handle, Settings.Size, zh ? Settings.Zh : Settings.En);
                    tray.Icon = zh ? iconZh : iconEn;
                    lastChinese = zh;
                }
            }

            // Watchdog: re-arm the mouse hook every ~10s in case Windows dropped it silently.
            if (++tickCount % 400 == 0) InstallHooks();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); }
            base.Dispose(disposing);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
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

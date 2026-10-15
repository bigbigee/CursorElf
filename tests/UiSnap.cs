using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;
using ImeCursorDot;

class UiSnap
{
    [STAThread]
    static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "ui_settings.png";
        Settings.Load();
        if (args.Length > 1) Settings.Dance = args[1] == "on";
        Application.EnableVisualStyles();
        using (SettingsForm f = new SettingsForm())
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-32000, 0);
            f.Show();
            for (int i = 0; i < 30; i++) Application.DoEvents();
            Thread.Sleep(250);
            for (int i = 0; i < 30; i++) Application.DoEvents();
            Bitmap bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            bmp.Save(outPath, ImageFormat.Png);
            bmp.Dispose();
        }
        Console.WriteLine("SAVED " + outPath);
    }
}

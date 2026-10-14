using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Generates the 光标Elf red/green yin-yang application icon (.ico) at build time.
// Usage: MakeYinIcon.exe <output.ico>
class MakeYinIcon
{
    static readonly Color Green = Color.FromArgb(34, 178, 92);
    static readonly Color Red = Color.FromArgb(224, 62, 62);

    static Bitmap Draw(int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float w = size, cx = w / 2f, cy = w / 2f;
            float r = w / 2f - Math.Max(1f, w / 32f);

            using (SolidBrush gb = new SolidBrush(Green))
                g.FillEllipse(gb, cx - r, cy - r, 2 * r, 2 * r);

            float sr = r / 2f; // small circle radius
            using (GraphicsPath red = new GraphicsPath())
            {
                red.AddArc(cx - r, cy - r, 2 * r, 2 * r, -90, 180);              // big right half: top -> bottom
                red.AddArc(cx - sr, cy, 2 * sr, 2 * sr, 90, -180);               // bottom small: via left side back to center
                red.AddArc(cx - sr, cy - r, 2 * sr, 2 * sr, 90, -180);           // top small: via right side to top
                red.CloseFigure();
                using (SolidBrush rb = new SolidBrush(Red)) g.FillPath(rb, red);
            }

            float dr = r / 6f; // eye radius
            using (SolidBrush rb = new SolidBrush(Red)) g.FillEllipse(rb, cx - dr, cy - sr - dr, 2 * dr, 2 * dr);
            using (SolidBrush gb = new SolidBrush(Green)) g.FillEllipse(gb, cx - dr, cy + sr - dr, 2 * dr, 2 * dr);

            using (Pen p = new Pen(Color.FromArgb(235, 20, 20, 20), Math.Max(1f, w / 28f)))
                g.DrawEllipse(p, cx - r, cy - r, 2 * r, 2 * r);
        }
        return bmp;
    }

    static byte[] BmpEntry(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] px = new byte[w * h * 4];
        for (int y = 0; y < h; y++) // bottom-up
            System.Runtime.InteropServices.Marshal.Copy(d.Scan0 + (h - 1 - y) * d.Stride, px, y * w * 4, w * 4);
        bmp.UnlockBits(d);

        int maskRow = ((w + 31) / 32) * 4;
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((ushort)1); bw.Write((ushort)32);
            bw.Write(0); bw.Write(px.Length); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            bw.Write(px);
            bw.Write(new byte[maskRow * h]);
            return ms.ToArray();
        }
    }

    static byte[] PngEntry(Bitmap bmp)
    {
        using (MemoryStream ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "guangbiaoelf.ico";
        int[] sizes = new int[] { 16, 24, 32, 48, 256 };
        byte[][] entries = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            entries[i] = sizes[i] >= 256 ? PngEntry(Draw(sizes[i])) : BmpEntry(Draw(sizes[i]));

        using (FileStream fs = new FileStream(outPath, FileMode.Create))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s)); bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((ushort)1); bw.Write((ushort)32);
                bw.Write(entries[i].Length); bw.Write(offset);
                offset += entries[i].Length;
            }
            for (int i = 0; i < sizes.Length; i++) bw.Write(entries[i]);
        }
        Console.WriteLine("ico written: " + outPath + " " + new FileInfo(outPath).Length + " bytes");
    }
}

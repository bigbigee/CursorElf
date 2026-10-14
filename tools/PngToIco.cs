using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// PngToIco.exe <source.png> <out.ico> [sizes...]   default sizes: 16 24 32 48 64 128 256
class PngToIco
{
    static void Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: PngToIco <src.png> <out.ico> [sizes]"); return; }
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        if (args.Length > 2)
        {
            string[] parts = args[2].Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            sizes = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) sizes[i] = int.Parse(parts[i]);
        }

        using (Bitmap src = new Bitmap(args[0]))
        {
            byte[][] payloads = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                payloads[i] = Render(src, sizes[i], sizes[i] >= 64);

            using (FileStream fs = new FileStream(args[1], FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((ushort)1); bw.Write((ushort)32);
                    bw.Write(payloads[i].Length);
                    bw.Write(offset);
                    offset += payloads[i].Length;
                }
                for (int i = 0; i < sizes.Length; i++) bw.Write(payloads[i]);
            }
        }
        Console.WriteLine("OK " + args[1]);
    }

    static Bitmap Scale(Bitmap src, int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY); // avoid transparent-edge smearing
                g.DrawImage(src, new Rectangle(0, 0, size, size),
                    0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
        }
        return bmp;
    }

    static byte[] Render(Bitmap src, int size, bool asPng)
    {
        using (Bitmap bmp = Scale(src, size))
        {
            if (asPng)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }

            int stride = size * 4;
            byte[] pixels = new byte[stride * size];
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, size, size),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            bmp.UnlockBits(data);

            byte[] result;
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write(40);            // BITMAPINFOHEADER size
                    bw.Write(size);          // biWidth
                    bw.Write(size * 2);      // biHeight = XOR + AND
                    bw.Write((ushort)1);     // biPlanes
                    bw.Write((ushort)32);    // biBitCount
                    bw.Write(0);             // BI_RGB
                    bw.Write(0);             // biSizeImage
                    bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                    for (int y = size - 1; y >= 0; y--) // bottom-up XOR rows
                        bw.Write(pixels, y * stride, stride);
                    bw.Write(new byte[stride * size]);  // AND mask all zero -> use alpha
                }
                result = ms.ToArray();
            }
            return result;
        }
    }
}

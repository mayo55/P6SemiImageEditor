using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace P6ImageConverter
{
    public static class ImageRendering
    {
        public static Bitmap Render(FontSet font, Palette palette, byte[] codes, byte[] attributes)
        {
            if (codes.Length != 800 || attributes.Length != 800) throw new ArgumentException("800文字必要です。");
            var bitmap = new Bitmap(320, 200, PixelFormat.Format24bppRgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, 320, 200),
                ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[960];
                for (int y = 0; y < 200; y++)
                {
                    for (int x = 0; x < 320; x++)
                    {
                        int cell = (y / 10) * 40 + x / 8;
                        int attr = attributes[cell];
                        int index = codes[cell] | ((attr & 128) << 1);
                        bool on = font.Glyphs[index].Mask[(y % 10) * 8 + x % 8];
                        int color = on ? (attr & 15) : (8 | ((attr >> 4) & 7));
                        Color c = palette.Colors[color];
                        row[x * 3] = c.B; row[x * 3 + 1] = c.G; row[x * 3 + 2] = c.R;
                    }
                    Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), row.Length);
                }
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }

        public static Bitmap AspectCorrect(Bitmap bitmap)
        {
            var result = new Bitmap(640, 480, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bitmap, new Rectangle(0, 0, 640, 480), 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel);
            }
            return result;
        }
    }
}

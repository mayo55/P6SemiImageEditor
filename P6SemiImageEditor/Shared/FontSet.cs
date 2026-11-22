using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;

namespace P6ImageConverter
{
    public sealed class Glyph
    {
        public readonly int Index;
        public readonly int[] OnPixels;
        public readonly bool[] Mask;
        internal readonly int InkWidth, InkHeight;
        internal readonly bool CompactIris;

        public Glyph(int index, bool[] mask)
        {
            Index = index;
            Mask = (bool[])mask.Clone();
            var points = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 80; i++) if (Mask[i]) points.Add(i);
            OnPixels = points.ToArray();
            if (OnPixels.Length == 0) return;
            int left=7, right=0, top=9, bottom=0;
            foreach (int p in OnPixels)
            {
                left=Math.Min(left,p%8); right=Math.Max(right,p%8);
                top=Math.Min(top,p/8); bottom=Math.Max(bottom,p/8);
            }
            InkWidth=right-left+1; InkHeight=bottom-top+1;
            CompactIris=OnPixels.Length<80 && 2*OnPixels.Length>=InkWidth*InkHeight && HasSingleStroke();
        }

        // A single iris must not split into disconnected marks within its white cell.
        private bool HasSingleStroke()
        {
            var seen=new bool[80]; var queue=new int[80];
            queue[0]=OnPixels[0]; seen[queue[0]]=true;
            int count=1;
            for (int i=0;i<count;i++)
            {
                int x=queue[i]%8,y=queue[i]/8;
                for (int yy=Math.Max(0,y-1);yy<=Math.Min(9,y+1);yy++)
                for (int xx=Math.Max(0,x-1);xx<=Math.Min(7,x+1);xx++)
                {
                    int p=yy*8+xx;
                    if (!seen[p] && Mask[p]) { seen[p]=true; queue[count++]=p; }
                }
            }
            return count==OnPixels.Length;
        }
    }

    public sealed class FontSet
    {
        public readonly Glyph[] Glyphs;
        public readonly string Source;
        public readonly string Sha256;

        private FontSet(Glyph[] glyphs, string source, string sha256)
        {
            Glyphs = glyphs;
            Source = source;
            Sha256 = sha256;
        }

        public static FontSet Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
            if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                return LoadAtlas(path, hash);
            if (data.Length != 8192)
                throw new InvalidDataException("CGROMは8192バイトが必要です。CGROM60m.62（またはCGROM62.62）を選択してください。PNGアトラスも利用できます。");
            var glyphs = new Glyph[512];
            for (int g = 0; g < 512; g++)
            {
                var mask = new bool[80];
                for (int y = 0; y < 10; y++)
                    for (int x = 0; x < 8; x++)
                        mask[y * 8 + x] = (data[g * 16 + y] & (0x80 >> x)) != 0;
                glyphs[g] = new Glyph(g, mask);
            }
            return new FontSet(glyphs, Path.GetFullPath(path), hash);
        }

        private static FontSet LoadAtlas(string path, string hash)
        {
            using (var bitmap = new Bitmap(path))
            {
                if (bitmap.Width != 256 || bitmap.Height != 160)
                    throw new InvalidDataException("フォントPNGは256×160ピクセル、32列×16行、1文字8×10、余白なしにしてください。");
                var glyphs = new Glyph[512];
                for (int g = 0; g < 512; g++)
                {
                    var mask = new bool[80];
                    for (int y = 0; y < 10; y++)
                        for (int x = 0; x < 8; x++)
                        {
                            Color c = bitmap.GetPixel((g % 32) * 8 + x, (g / 32) * 10 + y);
                            mask[y * 8 + x] = c.A >= 128 && (299 * c.R + 587 * c.G + 114 * c.B >= 128000);
                        }
                    glyphs[g] = new Glyph(g, mask);
                }
                return new FontSet(glyphs, Path.GetFullPath(path), hash);
            }
        }

        public Bitmap CreateAtlas()
        {
            var bitmap = new Bitmap(256, 160, PixelFormat.Format24bppRgb);
            for (int g = 0; g < 512; g++)
                for (int y = 0; y < 10; y++)
                    for (int x = 0; x < 8; x++)
                        bitmap.SetPixel((g % 32) * 8 + x, (g / 32) * 10 + y,
                            Glyphs[g].Mask[y * 8 + x] ? Color.White : Color.Black);
            return bitmap;
        }
    }
}

using System;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace P6ImageConverter
{
    public sealed class Palette
    {
        // Indices are hardware attribute nibbles, not BASIC COLOR numbers.
        // Index 0 is transparent and is excluded from the foreground search.
        public readonly Color[] Colors;
        public readonly string Source;

        public Palette(Color[] colors, string source)
        {
            if (colors.Length != 16) throw new ArgumentException("16色必要です。");
            if (colors[8].ToArgb() != Color.Black.ToArgb())
                throw new ArgumentException("色番号8は黒（000000）にしてください。");
            Colors = (Color[])colors.Clone();
            Source = source;
        }

        public static Palette Default()
        {
            // Digital RGB approximation. User may calibrate to the emulator/monitor.
            string[] hex = { "000000", "FF8000", "008000", "808000",
                "000080", "800080", "008080", "808080", "000000",
                "FF0000", "00FF00", "FFFF00", "0000FF", "FF00FF", "00FFFF", "FFFFFF" };
            var colors = new Color[16];
            for (int i = 0; i < 16; i++) colors[i] = ParseColor(hex[i]);
            return new Palette(colors, "digital-rgb-approximation");
        }

        public static Palette Load(string path)
        {
            var colors = new Color[16];
            var seen = new bool[16];
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] fields = line.Split(',');
                int index;
                if (fields.Length != 2 || !int.TryParse(fields[0].Trim(), out index)
                    || index < 0 || index > 15 || seen[index])
                    throw new InvalidDataException("パレットは番号0～15を各1回、番号,RRGGBB の形式で指定してください。");
                colors[index] = ParseColor(fields[1].Trim());
                seen[index] = true;
            }
            for (int i = 0; i < 16; i++)
                if (!seen[i]) throw new InvalidDataException("パレットの色番号 " + i + " がありません。");
            return new Palette(colors, Path.GetFullPath(path));
        }

        private static Color ParseColor(string hex)
        {
            int n;
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n))
                throw new InvalidDataException("色は6桁のRRGGBBで指定してください。");
            return Color.FromArgb((n >> 16) & 255, (n >> 8) & 255, n & 255);
        }

        public void Save(string path)
        {
            using (var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
            {
                writer.WriteLine("# hardware attribute index,RRGGBB; 0=transparent (excluded),8=black");
                for (int i = 0; i < 16; i++)
                    writer.WriteLine(i.ToString(CultureInfo.InvariantCulture) + "," +
                        (Colors[i].ToArgb() & 0xffffff).ToString("X6", CultureInfo.InvariantCulture));
            }
        }
    }
}

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using P6ImageConverter;

namespace P6SemiImageEditor
{
    [DataContract]
    internal class DisplaySettings
    {
        [DataMember] internal string Format { get; private set; }
        [DataMember] internal string FontSha256 { get; private set; }
        [DataMember] internal string FontSource { get; private set; }
        [DataMember] internal string[] PaletteRgb { get; private set; }

        internal static string ReadFor(string dataPath,ref FontSet font,ref Palette palette)
        {
            string prefix=ScreenFrame.Prefix(dataPath), meta=prefix+".json";
            if (!File.Exists(meta)) return "";
            DisplaySettings settings;
            using (var f=File.OpenRead(meta))
                settings=(DisplaySettings)new DataContractJsonSerializer(typeof(DisplaySettings)).ReadObject(f);
            if (settings==null || settings.Format!="P6-TEXT40x20-v1") return "";
            if (settings.PaletteRgb!=null)
            {
                if (settings.PaletteRgb.Length!=16) throw new InvalidDataException("パレットは16色必要です。");
                var colors=new Color[16];
                for (int i=0;i<16;i++)
                {
                    int rgb; string s=settings.PaletteRgb[i];
                    if (s==null || s.Length!=6 || !int.TryParse(s,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out rgb))
                        throw new InvalidDataException("パレットRGBの値が不正です。");
                    colors[i]=Color.FromArgb((rgb>>16)&255,(rgb>>8)&255,rgb&255);
                }
                palette=new Palette(colors,meta);
            }
            if (string.IsNullOrEmpty(settings.FontSha256) ||
                string.Equals(font.Sha256,settings.FontSha256,StringComparison.OrdinalIgnoreCase)) return "";
            if (!string.IsNullOrEmpty(settings.FontSource))
            {
                string dir=Path.GetDirectoryName(Path.GetFullPath(dataPath));
                // Try a font supplied beside the frame before the original local converter path.
                string local=Path.Combine(dir,Path.GetFileName(settings.FontSource));
                foreach (var name in new[]{local,settings.FontSource})
                {
                    if (!Path.IsPathRooted(name) || name.StartsWith(@"\\",StringComparison.Ordinal) || !File.Exists(name)) continue;
                    FontSet candidate=FontSet.Load(name);
                    if (string.Equals(candidate.Sha256,settings.FontSha256,StringComparison.OrdinalIgnoreCase))
                    {font=candidate;return "";}
                }
            }
            return "変換時のフォントと異なります。［表示］からフォントを選択できます。";
        }
    }
}

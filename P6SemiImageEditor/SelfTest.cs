using System;
using System.Drawing;
using System.IO;
using System.Linq;
using P6ImageConverter;

namespace P6SemiImageEditor
{
    internal static class SelfTest
    {
        private static void Check(bool ok,string test)
        {if (!ok) throw new InvalidOperationException("Self-test: "+test);}

        internal static void Run(FontSet font)
        {
            string dir=Path.Combine(Path.GetTempPath(),"p6-editor-test-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                for (int g=0;g<512;g++) for (int fg=0;fg<16;fg++) for (int bg=8;bg<16;bg++)
                {
                    int cell=ScreenFrame.Encode(g,fg,bg);
                    Check(ScreenFrame.GlyphIndex(cell)==g && ScreenFrame.Foreground(cell)==fg &&
                        ScreenFrame.Background(cell)==bg,"512 glyphs, 16 foregrounds, 8 backgrounds");
                }
                var doc=new ScreenFrame();
                for (int i=0;i<800;i++) doc.Set(i,ScreenFrame.Encode(i%512,1+i%15,8+i%8));
                doc.Set(0,ScreenFrame.Encode(0x125,1,15));
                doc.Set(1,ScreenFrame.Encode(0x180,0,8));
                doc.Set(799,ScreenFrame.Encode(0x12b,4,15));
                string frame=Path.Combine(dir,"s.frame.bin"),vram=Path.Combine(dir,"v.vram.bin");
                string pair=Path.Combine(dir,"p.chr.bin"),attr=Path.Combine(dir,"p.atr.bin");
                doc.Save(frame,false);byte[] original=File.ReadAllBytes(frame);
                Check(original.Length==1600 && original[0]==0xf1 && original[800]==0x25 &&
                    original[799]==0xf4 && original[1599]==0x2b,"hardware bank, packed offsets and last cell");
                doc.Save(vram,false);doc.Save(pair,false);
                byte[] v=File.ReadAllBytes(vram);Check(v.Length==2048 && v[1024]==0x25 && v[1823]==0x2b,"VRAM offset");
                v[801]=37;v[2047]=12;File.WriteAllBytes(vram,v);
                foreach (string p in new[]{frame,vram,pair,attr})
                    Check(original.SequenceEqual(ScreenFrame.Load(p).Bytes),"open "+Path.GetFileName(p));
                var working=ScreenFrame.Load(vram);
                working.Begin();working.Set(12,0xf402);working.Set(13,0xf401);working.End();
                Check(working.Dirty,"stroke dirty"); working.Undo();Check(!working.Dirty && working.CanRedo,"undo stroke");
                working.Redo();working.Save(vram,true);Check(!working.Dirty,"save point");
                var roundTrip=File.ReadAllBytes(vram);
                Check(roundTrip[801]==37 && roundTrip[2047]==12 && roundTrip[1036]==2,"VRAM gaps preserved");
                working.Undo();Check(working.Dirty,"undo across saved state");
                working.Begin();working.Set(12,0xf408);working.End();Check(!working.CanRedo,"editing a new history branch");
                int diff;Check(ScreenFrame.Closest(font.Glyphs[0x12b].Mask,font,0x12b,out diff)==0x12b && diff==0,"keep current glyph");
                var target=(bool[])font.Glyphs[0x125].Mask.Clone();target[2]=!target[2];
                int picked=ScreenFrame.Closest(target,font,0xa5,out diff);
                Check(diff<=1 && diff==target.Where((dot,i)=>dot!=font.Glyphs[picked].Mask[i]).Count(),"best available glyph");
                using (Bitmap bitmap=doc.Render(font,Palette.Default()))
                {
                    for (int y=0;y<10;y++) for (int x=0;x<8;x++)
                        Check(bitmap.GetPixel(x,y).ToArgb()==(font.Glyphs[0x125].Mask[y*8+x]?
                            Color.FromArgb(255,128,0):Color.White).ToArgb(),"orange/white checkerboard");
                    using (Bitmap aspect=ImageRendering.AspectCorrect(bitmap)) Check(aspect.Size==new Size(640,480),"4:3 PNG dimensions");
                }
                // A failure publishing the second member restores the first member.
                string blocked=Path.Combine(dir,"blocked");Directory.CreateDirectory(blocked);
                byte[] old=File.ReadAllBytes(attr);bool failed=false;
                try {AtomicFiles.Write(new[]{attr,blocked},new[]{new byte[800],new byte[800]},true);}
                catch (IOException) {failed=true;} catch (UnauthorizedAccessException) {failed=true;}
                Check(failed && old.SequenceEqual(File.ReadAllBytes(attr)),"rollback split file");
                var first=ScreenFrame.Load(frame);var second=ScreenFrame.Load(frame);
                first.Set(0,0xf401);first.Save(frame,true);failed=false;
                try {second.Save(frame,true);} catch (IOException) {failed=true;}
                Check(failed && ScreenFrame.Load(frame).Get(0)==0xf401,"external update protected");
                var displayFont=font;
                var displayPalette=Palette.Default();
                var colors=displayPalette.Colors.Select(c=>(c.ToArgb()&0xffffff).ToString("X6")).ToArray();
                colors[1]="123456";
                File.WriteAllText(ScreenFrame.Prefix(frame)+".json",
                    "{\"Format\":\"P6-TEXT40x20-v1\",\"FontSha256\":\""+font.Sha256+
                    "\",\"PaletteRgb\":[\""+string.Join("\",\"",colors)+"\"]}");
                string note=DisplaySettings.ReadFor(frame,ref displayFont,ref displayPalette);
                Check(note=="" && displayFont==font && (displayPalette.Colors[1].ToArgb()&0xffffff)==0x123456,"converter JSON palette");
                File.WriteAllBytes(frame,new byte[1599]);failed=false;
                try {ScreenFrame.Load(frame);} catch (InvalidDataException) {failed=true;}
                Check(failed,"truncated file rejected");
                using (var ui=new EditorForm(font,null)) Check(ui.Controls.Count>0,"editor construction");
                using (var dots=new DotDialog(font,Palette.Default(),doc.Get(0)))
                    Check(dots.ResultGlyph==0x125,"dot editor starts on the selected glyph");
            }
            finally {Directory.Delete(dir,true);}
        }
    }
}

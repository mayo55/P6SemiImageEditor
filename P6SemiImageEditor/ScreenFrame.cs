using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using P6ImageConverter;

namespace P6SemiImageEditor
{
    internal enum Layout { Packed, Vram, Pair }

    // Word layout used inside the editor: low byte = character, high byte = attribute.
    internal sealed class ScreenFrame
    {
        internal const int Cells = 800;
        internal byte[] Bytes { get; private set; }
        internal string FilePath { get; private set; }
        private byte[] saved, vramTemplate;
        private byte[] before;
        private readonly List<byte[]> undo = new List<byte[]>();
        private readonly Stack<byte[]> redo = new Stack<byte[]>();

        internal ScreenFrame()
        {
            Bytes = new byte[2*Cells];
            for (int i=0;i<Cells;i++) Set(i, Encode(0x20,15,8));
            MarkSaved();
        }

        internal static int Encode(int glyph,int foreground,int background)
        {
            if (glyph<0 || glyph>=512 || foreground<0 || foreground>15 || background<8 || background>15)
                throw new ArgumentOutOfRangeException("glyph", "文字または色番号が範囲外です。");
            int attribute=((glyph>>8)<<7)|((background&7)<<4)|foreground;
            return (attribute<<8)|(glyph&255);
        }
        internal static int GlyphIndex(int cell) { return (cell&255)|((cell>>7)&256); }
        internal static int Foreground(int cell) { return (cell>>8)&15; }
        internal static int Background(int cell) { return 8|((cell>>12)&7); }
        internal int Get(int index) { return (Bytes[index]<<8)|Bytes[Cells+index]; }
        internal void Set(int index,int cell)
        {
            Bytes[index]=(byte)(cell>>8);
            Bytes[Cells+index]=(byte)(cell&255);
        }
        internal bool Dirty { get { return !Bytes.SequenceEqual(saved); } }
        internal bool CanUndo { get { return undo.Count>0; } }
        internal bool CanRedo { get { return redo.Count>0; } }
        internal void Begin() { if (before==null) before=(byte[])Bytes.Clone(); }
        internal void End()
        {
            if (before!=null && !Bytes.SequenceEqual(before))
            {
                undo.Add(before);
                if (undo.Count>128) undo.RemoveAt(0);
                redo.Clear();
            }
            before=null;
        }
        internal void Undo()
        {
            End();
            if (!CanUndo) return;
            redo.Push(Bytes); Bytes=undo[undo.Count-1]; undo.RemoveAt(undo.Count-1);
        }
        internal void Redo()
        {
            End();
            if (!CanRedo) return;
            undo.Add(Bytes); Bytes=redo.Pop();
        }
        private void MarkSaved() { saved=(byte[])Bytes.Clone(); }

        internal static readonly string[] Suffixes={".frame.bin",".vram.bin",".chr.bin",".atr.bin"};
        internal static string Prefix(string path)
        {
            foreach (string suffix in Suffixes)
                if (path.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))
                    return path.Substring(0,path.Length-suffix.Length);
            throw new InvalidDataException("P6ImageConverter の .frame.bin、.vram.bin、.chr.bin または .atr.bin を指定してください。");
        }
        internal static Layout Format(string path)
        {
            string suffix=path.Substring(Prefix(path).Length);
            if (suffix.Equals(Suffixes[0],StringComparison.OrdinalIgnoreCase)) return Layout.Packed;
            if (suffix.Equals(Suffixes[1],StringComparison.OrdinalIgnoreCase)) return Layout.Vram;
            return Layout.Pair;
        }
        internal static string[] Members(string path)
        {
            if (Format(path)!=Layout.Pair) return new[]{path};
            string prefix=Prefix(path);
            return new[]{prefix+".atr.bin",prefix+".chr.bin"};
        }
        private static byte[] Read(string path,int size)
        {
            using (var f=File.OpenRead(path))
            {
                if (f.Length!=size) throw new InvalidDataException(Path.GetFileName(path)+" は "+size+" バイトの形式です。");
                using (var reader=new BinaryReader(f))
                {
                    byte[] bytes=reader.ReadBytes(size);
                    if (bytes.Length!=size) throw new EndOfStreamException();
                    return bytes;
                }
            }
        }
        internal static ScreenFrame Load(string path)
        {
            path=Path.GetFullPath(path);
            using (var gate=AcquireFileLock(path))
                return ReadDocument(path);
        }
        private static ScreenFrame ReadDocument(string path)
        {
            var doc=new ScreenFrame();
            Layout layout=Format(path);
            if (layout==Layout.Pair)
            {
                string[] members=Members(path);
                Array.Copy(Read(members[0],Cells),0,doc.Bytes,0,Cells);
                Array.Copy(Read(members[1],Cells),0,doc.Bytes,Cells,Cells);
            }
            else if (layout==Layout.Packed) doc.Bytes=Read(path,2*Cells);
            else
            {
                doc.vramTemplate=Read(path,2048);
                Array.Copy(doc.vramTemplate,doc.Bytes,Cells);
                Array.Copy(doc.vramTemplate,0x400,doc.Bytes,Cells,Cells);
            }
            doc.FilePath=path; doc.MarkSaved();
            return doc;
        }

        private byte[][] Serialize(Layout layout)
        {
            if (layout==Layout.Packed) return new[]{Bytes};
            if (layout==Layout.Pair) return new[]{Bytes.Take(Cells).ToArray(),Bytes.Skip(Cells).ToArray()};
            byte[] vram=vramTemplate==null?new byte[2048]:(byte[])vramTemplate.Clone();
            Array.Copy(Bytes,vram,Cells);
            Array.Copy(Bytes,Cells,vram,0x400,Cells);
            return new[]{vram};
        }
        internal void Save(string path,bool overwrite)
        {
            End();
            path=Path.GetFullPath(path);
            Layout layout=Format(path);
            byte[][] data=Serialize(layout);
            string[] paths=Members(path);
            using (var gate=AcquireFileLock(path))
            {
                if (FilePath!=null && string.Equals(Members(FilePath)[0],paths[0],StringComparison.OrdinalIgnoreCase))
                {
                    var disk=ReadDocument(path);
                    if (!saved.SequenceEqual(disk.Bytes) ||
                        (layout==Layout.Vram && !vramTemplate.SequenceEqual(disk.vramTemplate)))
                        throw new IOException("読み込み後にファイルが変更されました。別名で保存してください。");
                }
                AtomicFiles.Write(paths,data,overwrite);
                if (layout==Layout.Vram) vramTemplate=data[0];
                FilePath=path; MarkSaved();
            }
        }

        private sealed class FileLock : IDisposable
        {
            internal readonly Mutex Gate;
            internal FileLock(string key) { Gate=new Mutex(false,key); }
            public void Dispose() { Gate.ReleaseMutex(); Gate.Dispose(); }
        }
        private static FileLock AcquireFileLock(string path)
        {
            string key=Path.GetFullPath(Members(path)[0]).ToUpperInvariant();
            string hash;
            using (var sha=SHA256.Create()) hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key)));
            var handle=new FileLock("P6SemiImageEditor-"+hash);
            bool owned;
            try {owned=handle.Gate.WaitOne(0);}
            catch (AbandonedMutexException) {owned=true;}
            if (owned) return handle;
            handle.Gate.Dispose();
            throw new IOException("この画面は別のエディタが読み書きしています。もう一度お試しください。");
        }
        internal System.Drawing.Bitmap Render(FontSet font,Palette palette)
        {
            return ImageRendering.Render(font,palette,Bytes.Skip(Cells).ToArray(),Bytes.Take(Cells).ToArray());
        }
        internal static int Closest(bool[] target,FontSet font,int original,out int difference)
        {
            // Prefer the current character on an exact tie, then its bank and the lowest code.
            int best=original; difference=81;
            for (int k=-1;k<512;k++)
            {
                int g=k<0?original:(k^(original&256));
                int error=0;
                for (int p=0;p<80 && error<difference;p++)
                    if (target[p]!=font.Glyphs[g].Mask[p]) error++;
                if (error<difference) { best=g; difference=error; if (error==0) break; }
            }
            return best;
        }
    }

    internal static class AtomicFiles
    {
        // Stage on the destination volume and keep originals until both halves have been published.
        internal static void Write(string[] paths,byte[][] data,bool overwrite)
        {
            string dir=Path.GetDirectoryName(Path.GetFullPath(paths[0]));
            string stage=Path.Combine(dir,".p6edit-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var hadOriginal=new bool[paths.Length];
            int done=0;
            bool keepBackup=false;
            try
            {
                for (int i=0;i<paths.Length;i++)
                {
                    if (File.Exists(paths[i]) && !overwrite) throw new IOException("同名ファイルがあります。"+paths[i]);
                    File.WriteAllBytes(Path.Combine(stage,"new"+i),data[i]);
                }
                for (;done<paths.Length;done++)
                {
                    string dest=paths[done], source=Path.Combine(stage,"new"+done);
                    hadOriginal[done]=File.Exists(dest);
                    if (hadOriginal[done] && overwrite) File.Replace(source,dest,Path.Combine(stage,"old"+done));
                    else File.Move(source,dest);
                }
            }
            catch
            {
                try
                {
                    for (int i=done-1;i>=0;i--)
                        if (hadOriginal[i]) File.Replace(Path.Combine(stage,"old"+i),paths[i],null);
                        else File.Delete(paths[i]);
                }
                catch (Exception e)
                {
                    keepBackup=true;
                    throw new IOException("保存の復旧に失敗しました。退避先 "+stage,e);
                }
                throw;
            }
            finally { if (!keepBackup) Directory.Delete(stage,true); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using P6ImageConverter;

namespace P6SemiImageEditor
{
    internal static class HexKey
    {
        internal static int Digit(KeyEventArgs e)
        {
            if (e.Control || e.Alt) return -1;
            Keys key = e.KeyCode;
            if (key >= Keys.D0 && key <= Keys.D9) return key - Keys.D0;
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return key - Keys.NumPad0;
            if (key >= Keys.A && key <= Keys.F) return key - Keys.A + 10;
            return -1;
        }
    }

    internal static class PixelView
    {
        // Include both ends so a fast mouse drag leaves a continuous stroke.
        internal static IEnumerable<int> Line(int first,int last,int columns)
        {
            int x=first%columns,y=first/columns,toX=last%columns,toY=last/columns;
            int dx=Math.Abs(toX-x),dy=-Math.Abs(toY-y);
            int sx=x<toX?1:-1,sy=y<toY?1:-1,error=dx+dy;
            while (true)
            {
                yield return y*columns+x;
                if (x==toX && y==toY) yield break;
                int step=error*2;
                if (step>=dy) {error+=dy;x+=sx;}
                if (step<=dx) {error+=dx;y+=sy;}
            }
        }
        internal static void Nearest(Graphics g,Bitmap image,Rectangle dest)
        {
            g.InterpolationMode=InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode=PixelOffsetMode.Half;
            g.DrawImage(image,dest,0,0,image.Width,image.Height,GraphicsUnit.Pixel);
        }
        internal static void Mask(Graphics g,Rectangle rect,bool[] mask,Color fg,Color bg)
        {
            using (var ink=new SolidBrush(fg))
            using (var paper=new SolidBrush(bg))
            {
                g.FillRectangle(paper,rect);
                for (int y=0;y<10;y++) for (int x=0;x<8;x++)
                    if (mask[y*8+x])
                    {
                        int l=x*rect.Width/8,t=y*rect.Height/10;
                        g.FillRectangle(ink,rect.X+l,rect.Y+t,(x+1)*rect.Width/8-l,(y+1)*rect.Height/10-t);
                    }
            }
        }
    }

    internal sealed class ScreenCanvas : Control
    {
        private Bitmap image;
        private bool drawing;
        private int last=-1;
        internal int Selected;
        internal bool PaintMode,Grid;
        internal event Action<int> SelectCell;
        internal event Action<int,Point> CellMenuRequested;
        internal event Action<int,int> PaintLine;
        internal event Action StartStroke,FinishStroke,EditDots;

        internal ScreenCanvas()
        {
            DoubleBuffered=true; ResizeRedraw=true; TabStop=true; BackColor=Color.FromArgb(40,43,47);
            SetStyle(ControlStyles.Selectable,true); AccessibleName="編集画面";
            Cursor=Cursors.Cross;
        }
        internal void Display(Bitmap next)
        {
            var old=image; image=next;
            if (old!=null) old.Dispose();
            Invalidate();
        }
        protected override void Dispose(bool disposing)
        { if (disposing && image!=null) image.Dispose(); base.Dispose(disposing); }
        private Rectangle Area()
        {
            int unit=Math.Max(1,Math.Min(ClientSize.Width/80,ClientSize.Height/60));
            int w=80*unit,h=60*unit;
            return new Rectangle((Width-w)/2,(Height-h)/2,w,h);
        }
        private int Hit(Point pt)
        {
            Rectangle r=Area(); if (!r.Contains(pt)) return -1;
            return (pt.Y-r.Top)/(r.Height/20)*40+(pt.X-r.Left)/(r.Width/40);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (image==null) return;
            var r=Area(); int w=r.Width/40,h=r.Height/20;
            PixelView.Nearest(e.Graphics,image,r);
            if (Grid)
            using (var pen=new Pen(Color.FromArgb(104,135,142,151)))
            {
                for (int x=1;x<40;x++) e.Graphics.DrawLine(pen,r.X+x*w,r.Y,r.X+x*w,r.Bottom-1);
                for (int y=1;y<20;y++) e.Graphics.DrawLine(pen,r.X,r.Y+y*h,r.Right-1,r.Y+y*h);
            }
            var mark=new Rectangle(r.X+Selected%40*w,r.Y+Selected/40*h,w-1,h-1);
            e.Graphics.DrawRectangle(Pens.Black,mark);
            using (var pen=new Pen(Color.White) {DashStyle=DashStyle.Dash}) e.Graphics.DrawRectangle(pen,mark);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            int c=Hit(e.Location); if (c<0) return;
            if (e.Button!=MouseButtons.Left) return;
            if (PaintMode)
            {
                drawing=true; Capture=true; last=c;
                if (StartStroke!=null) StartStroke();
                if (PaintLine!=null) PaintLine(c,c);
            }
            else if (SelectCell!=null) SelectCell(c);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (!drawing) return;
            int c=Hit(e.Location);
            if (c>=0 && c!=last && PaintLine!=null) PaintLine(last<0?c:last,c);
            last=c;
        }
        internal void StopStroke()
        {
            if (!drawing) return;
            drawing=false; last=-1; Capture=false; if (FinishStroke!=null) FinishStroke();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button==MouseButtons.Left) StopStroke();
            if (e.Button!=MouseButtons.Right || CellMenuRequested==null) return;
            int cell=Hit(e.Location);
            if (cell>=0) CellMenuRequested(cell,e.Location);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { base.OnMouseCaptureChanged(e); if (!Capture) StopStroke(); }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (!PaintMode && e.Button==MouseButtons.Left && Hit(e.Location)>=0 && EditDots!=null) EditDots();
        }
        protected override bool IsInputKey(Keys key)
        {
            switch (key) { case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: return true; }
            return base.IsInputKey(key);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int x=Selected%40,y=Selected/40;
            switch (e.KeyCode)
            {
                case Keys.Left: x--; break; case Keys.Right: x++; break;
                case Keys.Up: y--; break; case Keys.Down: y++; break;
                case Keys.Enter: if (EditDots!=null) EditDots(); e.Handled=true; return;
                default: base.OnKeyDown(e); return;
            }
            int c=Math.Max(0,Math.Min(19,y))*40+Math.Max(0,Math.Min(39,x));
            if (SelectCell!=null) SelectCell(c); e.Handled=true;
        }
    }

    internal sealed class GlyphTable : Control
    {
        private FontSet fontSet;
        private Bitmap sheet;
        private bool awaitingLowDigit;
        internal int Bank,Current;
        internal event Action<int> Chosen;
        internal GlyphTable() { DoubleBuffered=true; ResizeRedraw=true; TabStop=true; AccessibleName="文字一覧（16進数）"; }
        internal void SetFontSet(FontSet f)
        {
            fontSet=f;
            if (sheet!=null) sheet.Dispose();
            // 16 x 16 grid for each bank, with hex row/column headers drawn separately.
            sheet=new Bitmap(128,320);
            using (var g=Graphics.FromImage(sheet))
                for (int i=0;i<512;i++)
                    PixelView.Mask(g,new Rectangle((i%16)*8,(i/16)*10,8,10),
                        f.Glyphs[i].Mask,Color.FromArgb(237,240,241),Color.FromArgb(36,40,45));
            Invalidate();
        }
        protected override void Dispose(bool disposing) { if (disposing && sheet!=null) sheet.Dispose(); base.Dispose(disposing); }
        private int CellWidth { get { return Math.Max(1,(Width-21)/16); } }
        private int CellHeight { get { return Math.Max(1,(Height-18)/16); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (fontSet==null) return;
            int w=CellWidth,h=CellHeight; var g=e.Graphics;
            g.InterpolationMode=InterpolationMode.NearestNeighbor; g.PixelOffsetMode=PixelOffsetMode.Half;
            g.DrawImage(sheet,new Rectangle(20,17,w*16,h*16),0,Bank*160,128,160,GraphicsUnit.Pixel);
            for (int i=0;i<16;i++)
            {
                TextRenderer.DrawText(g,i.ToString("X"),Font,new Rectangle(20+i*w,0,w,17),ForeColor,
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g,i.ToString("X"),Font,new Rectangle(0,17+i*h,19,h),ForeColor,
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            }
            if ((Current>>8)==Bank)
            {
                int code=Current&255;
                using (var pen=new Pen(Color.DeepSkyBlue,2))
                    g.DrawRectangle(pen,21+code%16*w,18+code/16*h,w-2,h-2);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            var area=new Rectangle(20,17,16*CellWidth,16*CellHeight);
            if (e.Button==MouseButtons.Left && area.Contains(e.Location) && Chosen!=null)
            {
                int column = (e.X - area.X) / CellWidth;
                int row = (e.Y - area.Y) / CellHeight;
                awaitingLowDigit = column == 0;
                Chosen(Bank * 256 + row * 16 + column);
            }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int digit = HexKey.Digit(e);
            if (digit < 0 || Chosen == null)
            {
                base.OnKeyDown(e);
                return;
            }

            int code = awaitingLowDigit ? (Current & 0xf0) + digit : digit << 4;
            awaitingLowDigit = !awaitingLowDigit;
            e.SuppressKeyPress = true;
            Chosen(Bank * 256 + code);
        }
        protected override void OnLostFocus(EventArgs e)
        {
            awaitingLowDigit = false;
            base.OnLostFocus(e);
        }
    }

    internal sealed class ColorBox : ComboBox
    {
        private readonly int first;
        private Palette palette;
        internal ColorBox(int start)
        {
            first=start; DropDownStyle=ComboBoxStyle.DropDownList; DrawMode=DrawMode.OwnerDrawFixed;
            ItemHeight=22; Dock=DockStyle.Fill; IntegralHeight=false; DropDownHeight=230;
            for (int c=first;c<=15;c++) Items.Add(c);
            SelectedIndex=0;
        }
        internal int Value { get {return first+SelectedIndex;} set {SelectedIndex=value-first;} }
        internal void SetPalette(Palette p) {palette=p; Invalidate();}
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int digit = HexKey.Digit(e);
            if (digit >= first)
            {
                e.SuppressKeyPress = true;
                Value = digit;
                return;
            }
            base.OnKeyDown(e);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            e.DrawBackground(); if (e.Index<0 || palette==null) return;
            int index=first+e.Index; Color c=palette.Colors[index];
            var swatch=new Rectangle(e.Bounds.X+3,e.Bounds.Y+4,22,Math.Max(5,e.Bounds.Height-8));
            using (var brush=new SolidBrush(c)) e.Graphics.FillRectangle(brush,swatch);
            e.Graphics.DrawRectangle(Pens.Gray,swatch);
            var area=new Rectangle(e.Bounds.X+31,e.Bounds.Y,e.Bounds.Width-31,e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics,string.Format("{0:X1}   #{1:X6}",index,c.ToArgb()&0xffffff),Font,area,e.ForeColor,
                TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding);
            e.DrawFocusRectangle();
        }
    }
}

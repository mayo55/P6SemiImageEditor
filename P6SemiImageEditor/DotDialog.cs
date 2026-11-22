using System;
using System.Drawing;
using System.Windows.Forms;
using P6ImageConverter;

namespace P6SemiImageEditor
{
    internal sealed class DotDialog : Form
    {
        private sealed class Pad : Control
        {
            internal bool[] Mask;
            internal Color Ink,Paper;
            internal bool Editable;
            internal event Action Changed;
            private MouseButtons held;
            private int previous=-1;
            internal Pad() {DoubleBuffered=true; ResizeRedraw=true; Dock=DockStyle.Fill;}
            private Rectangle Area()
            {
                int u=Math.Max(1,Math.Min((Width-10)/8,(Height-10)/10));
                return new Rectangle((Width-8*u)/2,(Height-10*u)/2,8*u,10*u);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                if (Mask==null) return;
                Rectangle a=Area(); PixelView.Mask(e.Graphics,a,Mask,Ink,Paper);
                if (!Editable) return;
                using (var p=new Pen(Color.FromArgb(140,127,127,127)))
                {
                    for (int i=0;i<=8;i++) e.Graphics.DrawLine(p,a.X+i*a.Width/8,a.Y,a.X+i*a.Width/8,a.Bottom);
                    for (int i=0;i<=10;i++) e.Graphics.DrawLine(p,a.X,a.Y+i*a.Height/10,a.Right,a.Y+i*a.Height/10);
                }
            }
            private void DrawAt(Point pt)
            {
                Rectangle a=Area();
                if (held==MouseButtons.None || !a.Contains(pt)) {previous=-1;return;}
                int dot=(pt.Y-a.Y)/(a.Height/10)*8+(pt.X-a.X)/(a.Width/8);
                bool ink=held==MouseButtons.Left;
                bool changed=false;
                foreach (int index in PixelView.Line(previous<0?dot:previous,dot,8))
                    if (Mask[index]!=ink) {Mask[index]=ink;changed=true;}
                previous=dot;
                if (changed) {Invalidate(); if (Changed!=null) Changed();}
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (!Editable || (e.Button!=MouseButtons.Left && e.Button!=MouseButtons.Right)) return;
                previous=-1; held=e.Button; Capture=true; DrawAt(e.Location);
            }
            protected override void OnMouseMove(MouseEventArgs e) {base.OnMouseMove(e); DrawAt(e.Location);}
            protected override void OnMouseUp(MouseEventArgs e) {base.OnMouseUp(e); held=MouseButtons.None; Capture=false;}
            protected override void OnMouseCaptureChanged(EventArgs e) {base.OnMouseCaptureChanged(e); if (!Capture) held=MouseButtons.None;}
        }
        internal int ResultGlyph {get;private set;}
        internal DotDialog(FontSet font,Palette palette,int cell)
        {
            Text="ドットから文字を選ぶ"; ClientSize=new Size(530,402); MinimumSize=new Size(460,365);
            StartPosition=FormStartPosition.CenterParent; ShowInTaskbar=false; AutoScaleMode=AutoScaleMode.Font;
            int original=ScreenFrame.GlyphIndex(cell);
            var left=new Pad {Editable=true, Mask=(bool[])font.Glyphs[original].Mask.Clone(),Cursor=Cursors.Cross};
            var right=new Pad();
            left.Ink=right.Ink=palette.Colors[ScreenFrame.Foreground(cell)];
            left.Paper=right.Paper=palette.Colors[ScreenFrame.Background(cell)];
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Padding=new Padding(10)};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,29)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            Controls.Add(layout);
            layout.Controls.Add(new Label {Text="希望する形　左で文字色／右で背景色",AutoSize=true},0,0);
            layout.Controls.Add(new Label {Text="保存される文字",AutoSize=true},1,0);
            layout.Controls.Add(left,0,1); layout.Controls.Add(right,1,1);
            var info=new Label {Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter};
            layout.Controls.Add(info,0,2);layout.SetColumnSpan(info,2);
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
            var cancel=new Button {Text="キャンセル",DialogResult=DialogResult.Cancel,AutoSize=true};
            var ok=new Button {Text="この文字を適用",DialogResult=DialogResult.OK,AutoSize=true};
            buttons.Controls.Add(cancel);buttons.Controls.Add(ok);
            layout.Controls.Add(buttons,0,3);layout.SetColumnSpan(buttons,2);AcceptButton=ok;CancelButton=cancel;
            Action update=delegate {
                int difference; ResultGlyph=ScreenFrame.Closest(left.Mask,font,original,out difference);
                right.Mask=font.Glyphs[ResultGlyph].Mask; right.Invalidate();
                info.Text=(ResultGlyph>=256?"セミ ":"通常 ")+(ResultGlyph&255).ToString("X2")+"H　　"+
                    (difference==0?"完全一致":"希望する形との差　"+difference+" / 80 ドット");
            };
            left.Changed+=update;update();
        }
    }
}

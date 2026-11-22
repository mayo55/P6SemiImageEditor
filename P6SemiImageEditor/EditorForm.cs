using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using P6ImageConverter;

namespace P6SemiImageEditor
{
    internal sealed class EditorForm : Form
    {
        private ScreenFrame doc=new ScreenFrame();
        private FontSet font,defaultFont;
        private Palette palette,defaultPalette=Palette.Default();
        private readonly ScreenCanvas canvas=new ScreenCanvas();
        private readonly Panel viewport=new Panel {Dock=DockStyle.Fill,AutoScroll=true};
        private readonly GlyphTable table=new GlyphTable {Dock=DockStyle.Fill};
        private readonly ComboBox bank=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        private readonly ColorBox ink=new ColorBox(0),paper=new ColorBox(8);
        private readonly RadioButton select=new RadioButton {Text="セルを編集",AutoSize=true,Checked=true};
        private readonly RadioButton brush=new RadioButton {Text="ブラシで描く",AutoSize=true};
        private readonly Panel sample=new Panel {Width=52,Height=58};
        private readonly Label brushInfo=new Label {AutoSize=true,Anchor=AnchorStyles.Left};
        private readonly ToolStripStatusLabel status=new ToolStripStatusLabel {Spring=true,TextAlign=ContentAlignment.MiddleLeft};
        private readonly ToolStripComboBox zoom=new ToolStripComboBox {DropDownStyle=ComboBoxStyle.DropDownList,AutoSize=false,Width=83};
        private readonly ToolStripButton grid=new ToolStripButton("グリッド") {CheckOnClick=true};
        private readonly ToolStripButton undoButton=new ToolStripButton("元に戻す"),redoButton=new ToolStripButton("やり直す");
        private ToolStripMenuItem undoMenu,redoMenu;
        private int brushCell=ScreenFrame.Encode(0x20,15,8);
        private bool syncing;
        private const string CellClipboardFormat = "P6SemiImageEditor.Cell";
        private const string InputFilter="P6文字画面 (*.frame.bin;*.vram.bin;*.chr.bin;*.atr.bin)|*.frame.bin;*.vram.bin;*.chr.bin;*.atr.bin";
        private const string OutputFilter="フレーム (*.frame.bin)|*.frame.bin|VRAM (*.vram.bin)|*.vram.bin|文字・属性ペア (*.chr.bin, *.atr.bin)|*.chr.bin";

        internal EditorForm(FontSet initial,string initialFile)
        {
            font=defaultFont=initial; palette=defaultPalette;
            Font=new Font("Yu Gothic UI",9F); AutoScaleMode=AutoScaleMode.Font;
            StartPosition=FormStartPosition.CenterScreen;MinimumSize=new Size(860,590);ClientSize=new Size(1070,680);
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=Padding.Empty,ColumnCount=1,RowCount=4};
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            var menu=new MenuStrip {Dock=DockStyle.Fill}; MainMenuStrip=menu; root.Controls.Add(menu,0,0);
            var file=new ToolStripMenuItem("ファイル(&F)");menu.Items.Add(file);
            AddItem(file,"新規",Keys.Control|Keys.N,NewFrame);
            AddItem(file,"開く…",Keys.Control|Keys.O,OpenDialog);
            AddItem(file,"保存",Keys.Control|Keys.S,delegate {Save(false);});
            AddItem(file,"名前を付けて保存…",Keys.Control|Keys.Shift|Keys.S,delegate {Save(true);});
            file.DropDownItems.Add(new ToolStripSeparator());
            AddItem(file,"画像をPNGに書き出す…",Keys.None,ExportImage);
            AddItem(file,"閉じる",Keys.Alt|Keys.F4,Close);
            var edit=new ToolStripMenuItem("編集(&E)");menu.Items.Add(edit);
            undoMenu=AddItem(edit,"元に戻す",Keys.Control|Keys.Z,Undo);
            redoMenu=AddItem(edit,"やり直す",Keys.Control|Keys.Y,Redo);
            edit.DropDownItems.Add(new ToolStripSeparator());
            AddItem(edit,"コピー",Keys.Control|Keys.C,CopyCell);
            AddItem(edit,"ペースト",Keys.Control|Keys.V,PasteCell);
            edit.DropDownItems.Add(new ToolStripSeparator());
            AddItem(edit,"ドットから文字を選ぶ…",Keys.Control|Keys.D,EditShape);
            var view=new ToolStripMenuItem("表示(&V)");menu.Items.Add(view);
            AddItem(view,"フォントROM・一覧PNGを選択…",Keys.None,LoadFont);
            AddItem(view,"パレットCSVを選択…",Keys.None,LoadPalette);
            var tools=new ToolStrip {GripStyle=ToolStripGripStyle.Hidden,Dock=DockStyle.Fill};root.Controls.Add(tools,0,1);
            tools.Items.Add("開く…",null,delegate {OpenDialog();});
            tools.Items.Add("保存",null,delegate {Save(false);});
            tools.Items.Add(new ToolStripSeparator());tools.Items.Add(undoButton);tools.Items.Add(redoButton);
            undoButton.Click+=delegate {Undo();};redoButton.Click+=delegate {Redo();};
            tools.Items.Add(new ToolStripSeparator());tools.Items.Add(new ToolStripLabel("表示倍率"));
            zoom.Items.AddRange(new object[]{"全体","1倍","2倍","3倍","4倍"});zoom.SelectedIndex=0;
            tools.Items.Add(zoom);tools.Items.Add(grid);
            grid.CheckedChanged+=delegate {canvas.Grid=grid.Checked;canvas.Invalidate();};
            var body=new TableLayoutPanel {ColumnCount=2,RowCount=1,Dock=DockStyle.Fill,Margin=Padding.Empty};
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,315));
            body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            root.Controls.Add(body,0,2);body.Controls.Add(viewport,0,0);viewport.Controls.Add(canvas);
            zoom.SelectedIndexChanged+=delegate {viewport.AutoScrollPosition=Point.Empty;SizeCanvas();};
            viewport.ClientSizeChanged+=delegate {SizeCanvas();};
            body.Controls.Add(MakeInspector(),1,0);
            var strip=new StatusStrip {Dock=DockStyle.Fill,SizingGrip=false};strip.Items.Add(status);root.Controls.Add(strip,0,3);
            canvas.SelectCell+=SelectAt;
            var cellMenu = new ContextMenuStrip();
            cellMenu.Items.Add("コピー", null, delegate { CopyCell(); });
            cellMenu.Items.Add("ペースト", null, delegate { PasteCell(); });
            cellMenu.Items.Add(new ToolStripSeparator());
            cellMenu.Items.Add("ブラシに採取", null, delegate { SampleAt(canvas.Selected); });
            canvas.CellMenuRequested += delegate(int cell, Point location)
            {
                canvas.StopStroke();
                SelectAt(cell);
                cellMenu.Show(canvas, location);
            };
            Disposed += delegate { cellMenu.Dispose(); };
            canvas.StartStroke+=delegate {doc.Begin();};canvas.PaintLine+=Stroke;canvas.FinishStroke+=delegate {doc.End();UpdateStatus();};
            canvas.EditDots+=EditShape;
            brush.CheckedChanged+=delegate {canvas.StopStroke();canvas.PaintMode=brush.Checked;if (!brush.Checked) SampleAt(canvas.Selected);else UpdateStatus();};
            table.Chosen+=delegate(int g) {SetBrush(ScreenFrame.Encode(g,ink.Value,paper.Value),true);};
            bank.SelectedIndexChanged+=delegate {table.Bank=bank.SelectedIndex;table.Invalidate();};
            bank.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                int digit = HexKey.Digit(e);
                if (digit != 0 && digit != 1) return;
                e.SuppressKeyPress = true;
                bank.SelectedIndex = digit;
            };
            ink.SelectedIndexChanged+=ColorChanged;paper.SelectedIndexChanged+=ColorChanged;
            AcceptDrop(this);AcceptDrop(canvas);
            Shown+=delegate {SizeCanvas(); if (!string.IsNullOrEmpty(initialFile)) OpenPath(initialFile);};
            FormClosing+=delegate(object sender,FormClosingEventArgs e) {if (!AllowLeave()) e.Cancel=true;};
            RefreshAssets();SampleAt(0);Render();
        }

        private static ToolStripMenuItem AddItem(ToolStripMenuItem parent,string text,Keys keys,Action action)
        {
            var item=new ToolStripMenuItem(text) {ShortcutKeys=keys};
            item.Click+=delegate {action();};parent.DropDownItems.Add(item);return item;
        }
        private Control MakeInspector()
        {
            var scroll=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(8,3,5,3)};
            var panel=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,RowCount=8};
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,65));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach (int size in new[]{30,64,32,32,33,285,37,42}) panel.RowStyles.Add(new RowStyle(SizeType.Absolute,size));
            scroll.Controls.Add(panel);
            var modes=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
            modes.Controls.Add(select);modes.Controls.Add(brush);panel.Controls.Add(modes,0,0);panel.SetColumnSpan(modes,2);
            panel.Controls.Add(sample,0,1);panel.Controls.Add(brushInfo,1,1);
            sample.Paint+=delegate(object s,PaintEventArgs e) {
                PixelView.Mask(e.Graphics,new Rectangle(1,1,48,56),font.Glyphs[ScreenFrame.GlyphIndex(brushCell)].Mask,
                    palette.Colors[ScreenFrame.Foreground(brushCell)],palette.Colors[ScreenFrame.Background(brushCell)]);
            };
            panel.Controls.Add(Caption("文字色"),0,2);panel.Controls.Add(ink,1,2);
            panel.Controls.Add(Caption("背景色"),0,3);panel.Controls.Add(paper,1,3);
            panel.Controls.Add(Caption("文字一覧"),0,4);bank.Items.AddRange(new object[]{"通常文字　00–FFH","セミグラフィック　00–FFH"});bank.SelectedIndex=0;panel.Controls.Add(bank,1,4);
            panel.Controls.Add(table,0,5);panel.SetColumnSpan(table,2);
            var dots=new Button {Text="選択セルをドットで編集…",AutoSize=true,Dock=DockStyle.Fill};
            dots.Click+=delegate {EditShape();};panel.Controls.Add(dots,0,6);panel.SetColumnSpan(dots,2);
            var hint=Caption("左クリックで選択・描画　右クリックでメニュー\r\n方向キーで移動　ダブルクリックでドット編集");
            panel.Controls.Add(hint,0,7);panel.SetColumnSpan(hint,2);
            return scroll;
        }
        private static Label Caption(string text) {return new Label {Text=text,AutoSize=true,Anchor=AnchorStyles.Left};}
        private void SizeCanvas()
        {
            int z=zoom.SelectedIndex;
            if (z<=0) {viewport.AutoScroll=false;canvas.Dock=DockStyle.Fill;}
            else {canvas.Dock=DockStyle.None;viewport.AutoScroll=true;canvas.Size=new Size(320*z,240*z);}
        }
        private void AcceptDrop(Control target)
        {
            target.AllowDrop=true;
            target.DragEnter+=delegate(object sender,DragEventArgs e) {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect=DragDropEffects.Copy;
            };
            target.DragDrop+=delegate(object sender,DragEventArgs e) {
                var paths=e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths!=null && paths.Length==1) OpenPath(paths[0]);
            };
        }
        private void ColorChanged(object sender,EventArgs e)
        { if (!syncing) SetBrush(ScreenFrame.Encode(ScreenFrame.GlyphIndex(brushCell),ink.Value,paper.Value),true); }
        private void SetBrush(int cell,bool apply)
        {
            brushCell=cell; int g=ScreenFrame.GlyphIndex(cell);
            syncing=true;
            try {ink.Value=ScreenFrame.Foreground(cell);paper.Value=ScreenFrame.Background(cell);bank.SelectedIndex=g>>8;}
            finally {syncing=false;}
            table.Current=g;table.Invalidate();sample.Invalidate();
            brushInfo.Text=(g>=256?"セミグラフィック":"通常文字")+"  "+(g&255).ToString("X2")+"H\r\n属性 "+(cell>>8).ToString("X2")+"H";
            if (apply && !canvas.PaintMode && doc.Get(canvas.Selected)!=cell)
            {doc.Begin();doc.Set(canvas.Selected,cell);doc.End();Render();}
            else UpdateStatus();
        }
        private void SelectAt(int cell)
        {
            canvas.Selected=cell;canvas.Invalidate();
            if (!canvas.PaintMode) SetBrush(doc.Get(cell),false);else UpdateStatus();
        }
        private void SampleAt(int cell) {canvas.Selected=cell;canvas.Invalidate();SetBrush(doc.Get(cell),false);}
        private void Stroke(int start,int end)
        {
            foreach (int index in PixelView.Line(start,end,40)) doc.Set(index,brushCell);
            canvas.Selected=end;Render();
        }
        private void Render() {canvas.Display(doc.Render(font,palette));UpdateStatus();}
        private void UpdateStatus()
        {
            Text=(doc.Dirty?"* ":"")+(doc.FilePath==null?"新規画面":Path.GetFileName(doc.FilePath))+"  —  P6SemiImageEditor";
            undoButton.Enabled=undoMenu.Enabled=doc.CanUndo;redoButton.Enabled=redoMenu.Enabled=doc.CanRedo;
            int n=canvas.Selected,cell=doc.Get(n),g=ScreenFrame.GlyphIndex(cell);
            status.Text=string.Format("セル ({0}, {1})   {2} {3:X2}H    文字色 {4:X}    背景色 {5:X}    属性 {6:X2}H",
                n%40,n/40,g>=256?"セミ":"通常",g&255,ScreenFrame.Foreground(cell),ScreenFrame.Background(cell),cell>>8);
        }
        private void Undo() {canvas.StopStroke();doc.Undo();SelectAt(canvas.Selected);Render();}
        private void Redo() {canvas.StopStroke();doc.Redo();SelectAt(canvas.Selected);Render();}

        private void CopyCell()
        {
            canvas.StopStroke();
            int cell = doc.Get(canvas.Selected);
            try
            {
                using (var bytes = new MemoryStream(new[] { (byte)(cell >> 8), (byte)(cell & 255) }))
                {
                    var data = new DataObject();
                    data.SetData(CellClipboardFormat, false, bytes);
                    Clipboard.SetDataObject(data, true);
                }
                status.Text = "選択セルをコピーしました。";
            }
            catch (ExternalException e) { ShowError(e); }
        }

        private void PasteCell()
        {
            canvas.StopStroke();
            int cell;
            try
            {
                var data = Clipboard.GetDataObject();
                using (var bytes = data == null ? null : data.GetData(CellClipboardFormat, false) as MemoryStream)
                {
                    if (bytes == null || bytes.Length != 2)
                    {
                        status.Text = "クリップボードに貼り付けられるセルがありません。";
                        return;
                    }
                    bytes.Position = 0;
                    cell = (bytes.ReadByte() << 8) | bytes.ReadByte();
                }
            }
            catch (ExternalException e) { ShowError(e); return; }

            if (doc.Get(canvas.Selected) == cell) return;
            doc.Begin();
            doc.Set(canvas.Selected, cell);
            doc.End();
            SelectAt(canvas.Selected);
            Render();
        }

        private void EditShape()
        {
            canvas.StopStroke();int c=canvas.Selected,old=doc.Get(c);
            using (var dialog=new DotDialog(font,palette,old))
            {
                if (dialog.ShowDialog(this)!=DialogResult.OK) return;
                doc.Begin();doc.Set(c,ScreenFrame.Encode(dialog.ResultGlyph,ScreenFrame.Foreground(old),ScreenFrame.Background(old)));doc.End();
                SampleAt(c);Render();
            }
        }
        private void RefreshAssets()
        {table.SetFontSet(font);ink.SetPalette(palette);paper.SetPalette(palette);sample.Invalidate();}
        private bool AllowLeave()
        {
            canvas.StopStroke(); if (!doc.Dirty) return true;
            DialogResult r=MessageBox.Show(this,"編集した画面を保存しますか？","P6SemiImageEditor",
                MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
            return r==DialogResult.No || (r==DialogResult.Yes && Save(false));
        }
        private void NewFrame()
        {
            if (!AllowLeave()) return;
            doc=new ScreenFrame();font=defaultFont;palette=defaultPalette;RefreshAssets();SampleAt(0);Render();
        }
        private void OpenDialog()
        {
            using (var d=new OpenFileDialog {Title="P6文字画面を開く",Filter=InputFilter,RestoreDirectory=true})
                if (d.ShowDialog(this)==DialogResult.OK) OpenPath(d.FileName);
        }
        private void OpenPath(string path)
        {
            if (!AllowLeave()) return;
            try
            {
                ScreenFrame next=ScreenFrame.Load(path);FontSet f=defaultFont;Palette p=defaultPalette;
                string note;
                try {note=DisplaySettings.ReadFor(path,ref f,ref p);}
                catch (Exception e) {note="表示設定をすべて読み込めませんでした。 "+e.Message;}
                doc=next;font=f;palette=p;RefreshAssets();SampleAt(0);Render();
                if (note.Length>0) MessageBox.Show(this,note,"表示設定",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
            catch (Exception e) {ShowError(e);}
        }
        private bool Save(bool asNew)
        {
            canvas.StopStroke();
            try
            {
                string path=doc.FilePath;
                if (asNew || path==null)
                {
                    using (var d=new SaveFileDialog {Title="文字画面を保存",Filter=OutputFilter,AddExtension=false,
                        OverwritePrompt=false,RestoreDirectory=true,FileName=path==null?"screen":Path.GetFileName(ScreenFrame.Prefix(path))})
                    {
                        if (path!=null) {d.InitialDirectory=Path.GetDirectoryName(path);d.FilterIndex=(int)ScreenFrame.Format(path)+1;}
                        if (d.ShowDialog(this)!=DialogResult.OK) return false;
                        path=d.FileName;
                        if (!ScreenFrame.Suffixes.Any(s=>path.EndsWith(s,StringComparison.OrdinalIgnoreCase)))
                            path+=ScreenFrame.Suffixes[d.FilterIndex-1];
                    }
                    string[] exist=ScreenFrame.Members(path).Where(File.Exists).ToArray();
                    bool overwrite=exist.Length>0;
                    if (overwrite && MessageBox.Show(this,"次のファイルを上書きしますか？\r\n"+string.Join("\r\n",exist),
                        "名前を付けて保存",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return false;
                    doc.Save(path,overwrite);
                }
                else doc.Save(path,true);
                UpdateStatus();return true;
            }
            catch (Exception e) {ShowError(e);return false;}
        }
        private void LoadFont()
        {
            using (var d=new OpenFileDialog {Title="変換に使ったフォントを選択",Filter="フォント一覧PNG・CGROM|*.png;*.60;*.61;*.62;*.66;*.bin;*.rom|すべてのファイル|*.*"})
                if (d.ShowDialog(this)==DialogResult.OK)
                    try {font=defaultFont=FontSet.Load(d.FileName);RefreshAssets();Render();}
                    catch (Exception e) {ShowError(e);}
        }
        private void LoadPalette()
        {
            using (var d=new OpenFileDialog {Title="P6ImageConverter用のパレットを選択",Filter="パレットCSV|*.csv"})
                if (d.ShowDialog(this)==DialogResult.OK)
                    try {palette=defaultPalette=Palette.Load(d.FileName);RefreshAssets();Render();}
                    catch (Exception e) {ShowError(e);}
        }
        private void ExportImage()
        {
            using (var d=new SaveFileDialog {Title="4:3の画像を書き出す",Filter="PNG画像|*.png",DefaultExt="png",FileName="screen.png"})
                if (d.ShowDialog(this)==DialogResult.OK)
                    try
                    {
                        using (var native=doc.Render(font,palette))
                        using (var aspect=ImageRendering.AspectCorrect(native))
                        using (var buffer=new MemoryStream())
                        {
                            aspect.Save(buffer,ImageFormat.Png);
                            AtomicFiles.Write(new[]{d.FileName},new[]{buffer.ToArray()},true);
                        }
                    }
                    catch (Exception e) {ShowError(e);}
        }
        private void ShowError(Exception e) {MessageBox.Show(this,e.Message,"P6SemiImageEditor",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
}

using System;
using System.IO;
using System.Windows.Forms;
using P6ImageConverter;

namespace P6SemiImageEditor
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string atlas=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"font-atlas.png");
                FontSet font=FontSet.Load(atlas);
                if (args.Length==1 && args[0]=="--self-test") {SelfTest.Run(font);return 0;}
                if (args.Length>1) throw new ArgumentException("開く文字画面を1ファイル指定してください。");
                using (var form=new EditorForm(font,args.Length==0?null:args[0])) Application.Run(form);
                return 0;
            }
            catch (Exception e)
            {
                if (args.Length==1 && args[0]=="--self-test")
                {
                    Console.Error.WriteLine(e.ToString());
                    try {File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-error.txt"),e.ToString());}
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                else MessageBox.Show(e.Message,"P6SemiImageEditor",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}

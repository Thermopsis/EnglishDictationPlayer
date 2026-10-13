using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("英语听写播放器")]
[assembly: AssemblyProduct("英语听写播放器")]
[assembly: AssemblyCompany("ThermoXu")]
[assembly: AssemblyVersion("1.1.0.0")]

namespace PhrasePlayer {
    static class Program {
        [STAThread]
        static void Main(string[] args) {
            if (args.Length > 0 && args[0] == "--self-test") { Environment.ExitCode = Tests.Run(args.Length > 1 ? args[1] : Environment.CurrentDirectory); return; }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e) { MessageBox.Show(e.Exception.Message, Application.OpenForms.Count > 0 ? Application.OpenForms[0].Text : "英语听写播放器", MessageBoxButtons.OK, MessageBoxIcon.Warning); };
#if UI_TEST
            bool uiTest = args.Length == 3 && args[0] == "--ui-test";
            Application.Run(new PlayerForm(uiTest ? args[1] : args.Length > 0 ? args[0] : null, uiTest ? args[2] : null));
#else
            // A release always uses audible playback; silent GUI tests require a separate build.
            Application.Run(new PlayerForm(args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null, null));
#endif
        }
    }
}

using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using TodoWidget.Core;
using TodoWidget.UI;

namespace TodoWidget
{
    static class Program
    {
        const string MutexName = @"Local\TodoWidget.SingleInstance.7d2f9a";
        const string ShowEventName = @"Local\TodoWidget.Show.7d2f9a";

        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 이미 실행 중이면 기존 위젯을 앞으로 불러오고 종료
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(ShowEventName)) ev.Set();
                    }
                    catch (Exception) { }
                    return;
                }

                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += (s, e) => Log(e.Exception);
                    AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);

                    Theme.Init();
                    Theme.GlassKeySymmetric = Environment.GetEnvironmentVariable("ILHAERA_GLASS_KEY") == "symmetric";
                    Storage storage;
                    AppData data;
                    try
                    {
                        storage = new Storage(Storage.DefaultDirectory);
                        data = storage.Load();
                    }
                    catch (Exception ex)
                    {
                        Log(ex);
                        MessageBox.Show("데이터 폴더를 열 수 없어요.\n" + ex.Message, "일해라",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    using (var app = new TrayApp(storage, data, showEvent))
                        Application.Run(app);
                }
                GC.KeepAlive(mutex);
            }
        }

        public static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Storage.DefaultDirectory);
                var path = Path.Combine(Storage.DefaultDirectory, "error.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}

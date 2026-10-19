using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace BgClicker
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // The chat/agent opens files in a low-integrity sandbox where the
            // browser cannot be started. Do not run a half-working instance:
            // offer to relaunch through Explorer instead.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DSH_SHELL")))
            {
                DialogResult answer = MessageBox.Show(
                    "Кликер открыт из окна чата. Windows ограничила его права: в таком режиме " +
                    "нельзя запустить браузер и сохранить настройки.\n\n" +
                    "Нажмите «Да», чтобы перезапустить через Проводник. Если не сработает — " +
                    "закройте это окно и запустите кликер из меню «Пуск» (Win → «Фоновый кликер»).",
                    "Фоновый кликер",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (answer == DialogResult.Yes)
                {
                    try
                    {
                        string installed = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "BgClicker",
                            "BgClicker.exe");
                        string target = File.Exists(installed)
                            ? installed
                            : Application.ExecutablePath;

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = "\"" + target + "\"",
                            UseShellExecute = false
                        });
                    }
                    catch
                    {
                    }
                }

                return;
            }

            // Low-integrity instances must not block a normal launch.
            string mutexName = Native.IsLowIntegrity()
                ? "BgClicker_LowInstance_v1"
                : "BgClicker_SingleInstance_v1";

            // When started normally, close leftover restricted instances so the
            // user is not looking at an old "limited mode" window.
            if (!Native.IsLowIntegrity())
            {
                int currentId = Process.GetCurrentProcess().Id;
                foreach (Process other in Process.GetProcessesByName("BgClicker"))
                {
                    if (other.Id == currentId)
                    {
                        continue;
                    }

                    int integrity = Native.GetProcessIntegrity(other.Id);
                    if (integrity > 0 && integrity < 0x2000)
                    {
                        try
                        {
                            other.Kill();
                        }
                        catch
                        {
                        }
                    }
                }
            }

            bool createdNew;
            using (var mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    IntPtr existing = Native.FindWindow(null, "Фоновый кликер");
                    if (existing != IntPtr.Zero)
                    {
                        // The first instance is probably hidden in the tray:
                        // ask it to show its window instead of running twice.
                        Native.PostMessage(existing, Native.WM_SHOW_APP, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }

                    MessageBox.Show(
                        "Фоновый кликер уже запущен, но его окно не найдено.\n\n" +
                        "Откройте скрытые значки в трее (стрелка ^ рядом с часами), найдите синюю иконку " +
                        "кликера и дважды щёлкните по ней. Либо завершите процесс BgClicker.exe в Диспетчере " +
                        "задач и запустите кликер снова.",
                        "Фоновый кликер",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try
                {
                    Application.Run(new MainForm());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Критическая ошибка:\n\n" + ex,
                        "Фоновый кликер",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                GC.KeepAlive(mutex);
            }
        }
    }
}

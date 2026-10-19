using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace BgClicker
{
    public static class BrowserLauncher
    {
        public const int DebugPort = 9222;

        public static string FindBrowser()
        {
            foreach (string name in new[] { "chrome.exe", "msedge.exe" })
            {
                string fromRegistry = FromAppPaths(name);
                if (fromRegistry != null)
                {
                    return fromRegistry;
                }
            }

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string[] candidates =
            {
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")
            };

            foreach (string path in candidates)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        public static void Launch(string executable, bool separateProfile)
        {
            var arguments = new List<string>
            {
                "--remote-debugging-port=" + DebugPort,
                "--remote-debugging-address=127.0.0.1",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-renderer-backgrounding",
                "--disable-features=CalculateNativeWinOcclusion",
                "--no-first-run",
                "--no-default-browser-check"
            };

            if (separateProfile)
            {
                arguments.Add("--user-data-dir=" + GetProfileDirectory());
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                Arguments = string.Join(" ", arguments.Select(Quote))
            };

            Process.Start(startInfo);
        }

        public static string LastProfileDirectory { get; private set; }

        public static bool IsRestrictedEnvironment(out string error)
        {
            error = null;
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BgClicker");
                Directory.CreateDirectory(dir);

                string probe = Path.Combine(dir, ".write-probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return true;
            }
        }

        private static string GetProfileDirectory()
        {
            string[] candidates =
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BgClicker",
                    "ChromeProfile"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BgClicker",
                    "ChromeProfile"),
                Path.Combine(Path.GetTempPath(), "BgClickerChromeProfile"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfile")
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    Directory.CreateDirectory(candidate);
                    LastProfileDirectory = candidate;
                    return candidate;
                }
                catch
                {
                }
            }

            throw new InvalidOperationException("Не удалось создать папку профиля для браузера");
        }

        private static string Quote(string argument)
        {
            return argument.IndexOf(' ') >= 0 ? "\"" + argument + "\"" : argument;
        }

        private static string FromAppPaths(string executable)
        {
            foreach (RegistryKey root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using (RegistryKey key = root.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + executable))
                    {
                        if (key == null)
                        {
                            continue;
                        }

                        string path = key.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            return path;
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }
    }
}

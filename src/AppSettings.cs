using System;
using System.IO;
using System.Web.Script.Serialization;

namespace BgClicker
{
    public sealed class AppSettings
    {
        public string Mode { get; set; }
        public int Cps { get; set; }
        public int Jitter { get; set; }
        public int HotkeyVk { get; set; }
        public string BrowserUrl { get; set; }
        public int PointX { get; set; }
        public int PointY { get; set; }
        public long HwndDeep { get; set; }
        public long HwndRoot { get; set; }
        public int DeepX { get; set; }
        public int DeepY { get; set; }
        public int RootX { get; set; }
        public int RootY { get; set; }
        public string WindowTitle { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool SeparateProfile { get; set; }
        public bool UseDeepChild { get; set; }
        public string Theme { get; set; }

        public AppSettings()
        {
            Mode = "browser";
            Cps = 10;
            Jitter = 0;
            HotkeyVk = 0x77; // F8
            BrowserUrl = "";
            PointX = 100;
            PointY = 100;
            WindowTitle = "";
            MinimizeToTray = true;
            SeparateProfile = true;
            UseDeepChild = true;
            Theme = "dark";
        }

        private static string StorageDir
        {
            get
            {
                try
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "BgClicker");
                    Directory.CreateDirectory(dir);
                    return dir;
                }
                catch
                {
                    return AppDomain.CurrentDomain.BaseDirectory;
                }
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(StorageDir, "settings.json"); }
        }

        public static AppSettings Load()
        {
            try
            {
                string path = SettingsPath;
                if (File.Exists(path))
                {
                    var serializer = new JavaScriptSerializer();
                    AppSettings loaded = serializer.Deserialize<AppSettings>(File.ReadAllText(path));
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch
            {
                // Broken or unreadable settings: fall back to defaults.
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                File.WriteAllText(SettingsPath, serializer.Serialize(this));
            }
            catch
            {
                // Saving settings is not critical.
            }
        }
    }
}

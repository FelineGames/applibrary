using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace AppLibrary
{
    public static class AppStorage
    {
        public static string BaseFolderPath { get; }
        private static readonly string SettingsFilePath;
        private static Dictionary<string, string> _settingsCache = new Dictionary<string, string>();

        static AppStorage()
        {
            try
            {
                // Works if running as a packaged MSIX or Uno platform container
                BaseFolderPath = ApplicationData.Current.LocalFolder.Path;
            }
            catch
            {
                // Bulletproof fallback for unpackaged Windows .exe and native Arch Linux builds
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localAppData))
                {
                    localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
                }
                BaseFolderPath = Path.Combine(localAppData, "AppLibrary");
            }

            Directory.CreateDirectory(BaseFolderPath);
            SettingsFilePath = Path.Combine(BaseFolderPath, "app_settings.json");
            LoadSettings();
        }

        private static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (loaded != null)
                    {
                        _settingsCache = loaded;
                    }
                }
            }
            catch
            {
                _settingsCache = new Dictionary<string, string>();
            }
        }

        private static void SaveSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(_settingsCache, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }

        public static string GetString(string key, string defaultValue = "")
        {
            return _settingsCache.TryGetValue(key, out string val) && val != null ? val : defaultValue;
        }

        public static void SetString(string key, string value)
        {
            _settingsCache[key] = value ?? "";
            SaveSettings();
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            if (_settingsCache.TryGetValue(key, out string val) && bool.TryParse(val, out bool result))
            {
                return result;
            }
            return defaultValue;
        }

        public static void SetBool(string key, bool value)
        {
            _settingsCache[key] = value.ToString();
            SaveSettings();
        }
    }

    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            MainWindow = new Window();
            MainWindow.Title = "App Library";

            Frame rootFrame = new Frame();
            rootFrame.Navigate(typeof(MainPage), args.Arguments);

            MainWindow.Content = rootFrame;
            MainWindow.Activate();
        }
    }
}
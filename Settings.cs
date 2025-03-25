using System;
using System.IO;
using System.Text.Json;

namespace SquareSnap
{
    public class Settings
    {
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SquareSnap",
            "settings.json");

        public string? DefaultSaveLocation { get; set; }
        public int NextFileNumber { get; set; } = 1;

        // Get the next file name in the format SquareCap-YYYYMMDD###
        public string GetNextFileName()
        {
            string dateStr = DateTime.Now.ToString("yyyyMMdd");
            string numberStr = NextFileNumber.ToString("D3"); // Pad with leading zeros to 3 digits
            
            // Increment the file number for next time
            NextFileNumber++;
            Save();
            
            return $"SquareCap-{dateStr}{numberStr}";
        }

        // Singleton instance
        private static Settings? _instance;
        public static Settings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }
                return _instance;
            }
        }

        // Load settings from file
        private static Settings Load()
        {
            try
            {
                // Create directory if it doesn't exist
                string? directory = Path.GetDirectoryName(SettingsFilePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Load settings from file if it exists
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<Settings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception)
            {
                // Ignore errors and return default settings
            }

            // Return default settings
            return new Settings();
        }

        // Save settings to file
        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception)
            {
                // Ignore errors
            }
        }
    }
}

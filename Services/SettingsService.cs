using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ProtoLink.Windows.Messanger.Models;

namespace ProtoLink.Windows.Messanger.Services
{
    public interface ISettingsService
    {
        AppSettings LoadSettings();
        void SaveSettings(AppSettings settings);
    }

    public class SettingsService : ISettingsService
    {
        private readonly string _filePath;
        private readonly ILogger<SettingsService> _logger;

        public SettingsService(ILogger<SettingsService> logger)
        {
            _logger = logger;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "ProtoLinkMessenger");
            Directory.CreateDirectory(appFolder);
            _filePath = Path.Combine(appFolder, "settings.json");
            _logger.LogDebug("Settings file path: {FilePath}", _filePath);
        }

        public AppSettings LoadSettings()
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("Settings file not found, using default settings");
                return new AppSettings();
            }
            try
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                _logger.LogInformation("Settings loaded successfully from {FilePath}", _filePath);
                return settings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings from {FilePath}", _filePath);
                return new AppSettings();
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var json = JsonConvert.SerializeObject(settings);
                File.WriteAllText(_filePath, json);
                _logger.LogInformation("Settings saved successfully to {FilePath}", _filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings to {FilePath}", _filePath);
                throw;
            }
        }
    }
}


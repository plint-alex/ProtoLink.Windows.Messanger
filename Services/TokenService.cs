using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ProtoLink.Windows.Messanger.Models;

namespace ProtoLink.Windows.Messanger.Services
{
    public interface ITokenService
    {
        void SaveToken(TokenData data);
        TokenData? LoadToken();
        void ClearToken();
    }

    public class TokenService : ITokenService
    {
        private readonly string _filePath;
        private readonly ILogger<TokenService> _logger;

        public TokenService(ILogger<TokenService> logger)
        {
            _logger = logger;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "ProtoLinkMessenger");
            Directory.CreateDirectory(appFolder);
            _filePath = Path.Combine(appFolder, "token.dat");
            _logger.LogDebug("Token file path: {FilePath}", _filePath);
        }

        public void SaveToken(TokenData data)
        {
            try
            {
                var json = JsonConvert.SerializeObject(data);
                var plainBytes = Encoding.UTF8.GetBytes(json);
                var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_filePath, encryptedBytes);
                _logger.LogInformation("Token saved successfully for user: {Login}", data.Login);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save token to {FilePath}", _filePath);
                throw;
            }
        }

        public TokenData? LoadToken()
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogDebug("Token file not found at {FilePath}", _filePath);
                return null;
            }

            try
            {
                var encryptedBytes = File.ReadAllBytes(_filePath);
                var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(plainBytes);
                var token = JsonConvert.DeserializeObject<TokenData>(json);
                if (token != null)
                {
                    _logger.LogDebug("Token loaded successfully for user: {Login}", token.Login);
                }
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load token from {FilePath}", _filePath);
                return null;
            }
        }

        public void ClearToken()
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
                _logger.LogInformation("Token cleared successfully");
            }
        }
    }
}


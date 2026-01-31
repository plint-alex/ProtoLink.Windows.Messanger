using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Input;
using ProtoLink.Windows.Messanger.Services;
using ProtoLink.Windows.Messanger.Models;
using Microsoft.Extensions.Logging;

namespace ProtoLink.Windows.Messanger.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IAuthService _authService;
        private readonly ITokenService _tokenService;
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private string _apiBaseAddress;

        public SettingsViewModel(ISettingsService settingsService, IAuthService authService, ITokenService tokenService, HttpClient httpClient, ILogger logger)
        {
            _settingsService = settingsService;
            _authService = authService;
            _tokenService = tokenService;
            _httpClient = httpClient;
            _logger = logger;
            var settings = _settingsService.LoadSettings();
            _apiBaseAddress = settings.ApiBaseAddress;

            SaveCommand = new RelayCommand(_ => SaveSettings());
            LogOffCommand = new RelayCommand(_ => LogOff(), _ => _authService.IsAuthenticated);
            CloseCommand = new RelayCommand(_ => OnCloseRequested?.Invoke());

            LoginViewModel = new LoginViewModel(_authService);
            LoginViewModel.OnLoginSuccess += () => OnLoginSuccess?.Invoke();

            AddContactViewModel = new AddContactViewModel(_authService, _httpClient, _logger, isEmbedded: true);
            AddContactViewModel.OnContactAdded += () => OnContactAdded?.Invoke();
        }

        public string ApiBaseAddress
        {
            get => _apiBaseAddress;
            set { _apiBaseAddress = value; OnPropertyChanged(); }
        }

        public bool IsAuthenticated => _authService.IsAuthenticated;
        public string? CurrentLogin => _authService.CurrentToken?.Login;

        public LoginViewModel LoginViewModel { get; }
        public AddContactViewModel AddContactViewModel { get; }

        public ICommand SaveCommand { get; }
        public ICommand LogOffCommand { get; }
        public ICommand CloseCommand { get; }

        public event Action? OnLoginSuccess;
        public event Action? OnContactAdded;
        public event Action? OnSettingsSaved;
        public event Action? OnCloseRequested;

        private void SaveSettings()
        {
            var settings = new AppSettings { ApiBaseAddress = ApiBaseAddress };
            _settingsService.SaveSettings(settings);
            OnSettingsSaved?.Invoke();
        }

        private void LogOff()
        {
            _authService.Logout();
            // Notify that authentication state has changed
            OnPropertyChanged(nameof(IsAuthenticated));
            OnPropertyChanged(nameof(CurrentLogin));
            // Force UI refresh to show login
            OnSettingsSaved?.Invoke();
        }
    }
}


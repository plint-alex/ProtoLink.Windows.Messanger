using System.Windows;
using ProtoLink.Windows.Messanger.Services;
using ProtoLink.Windows.Messanger.Views;
using ProtoLink.Windows.Messanger.ViewModels;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace ProtoLink.Windows.Messanger
{
    public partial class MainWindow : Window
    {
        private readonly IAuthService _authService;
        private readonly ITokenService _tokenService;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<MainWindow> _logger;
        private HttpClient _httpClient;

        public MainWindow()
        {
            InitializeComponent();

            // Get logger factory from App
            var loggerFactory = App.LoggerFactory;
            _logger = loggerFactory.CreateLogger<MainWindow>();

            _logger.LogInformation("Initializing MainWindow");

            _tokenService = new TokenService(loggerFactory.CreateLogger<TokenService>());
            _settingsService = new SettingsService(loggerFactory.CreateLogger<SettingsService>());
            
            var settings = _settingsService.LoadSettings();
            _httpClient = new HttpClient { BaseAddress = new Uri(settings.ApiBaseAddress) };
            _authService = new AuthService(_httpClient, _tokenService, loggerFactory.CreateLogger<AuthService>());

            _logger.LogInformation("MainWindow initialized, API Base Address: {BaseAddress}", settings.ApiBaseAddress);

            ShowMessenger();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            ShowSettings();
        }

        private void AddContact_Click(object sender, RoutedEventArgs e)
        {
            ShowAddContact();
        }

        private void ShowSettings()
        {
            _logger.LogDebug("Showing settings view");
            var settings = _settingsService.LoadSettings();
            var handler = new AuthHandler(_tokenService, () => _authService, App.LoggerFactory.CreateLogger<AuthHandler>());
            handler.InnerHandler = new HttpClientHandler();
            var settingsHttpClient = new HttpClient(handler) { BaseAddress = new Uri(settings.ApiBaseAddress) };

            var vm = new SettingsViewModel(_settingsService, _authService, _tokenService, settingsHttpClient, _logger);
            vm.OnLoginSuccess += ShowMessenger;
            vm.OnCloseRequested += ShowMessenger;
            vm.OnContactAdded += ShowMessenger;
            vm.OnSettingsSaved += () => {
                // Refresh HttpClient if address changed
                var updatedSettings = _settingsService.LoadSettings();
                _logger.LogInformation("Settings saved, updating API Base Address to: {BaseAddress}", updatedSettings.ApiBaseAddress);
                _httpClient = new HttpClient { BaseAddress = new Uri(updatedSettings.ApiBaseAddress) };
                // We might need to recreate AuthService too if it's tied to HttpClient
                // For simplicity, let's just refresh the UI
                ShowSettings();
            };
            MainGrid.Children.Clear();
            MainGrid.Children.Add(new SettingsView { DataContext = vm });
        }

        private void ShowMessenger()
        {
            if (!_authService.IsAuthenticated)
            {
                _logger.LogInformation("User not authenticated, showing login prompt");
                MainGrid.Children.Clear();
                MainGrid.Children.Add(new System.Windows.Controls.TextBlock {
                    Text = "Please login in settings.",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                return;
            }

            _logger.LogInformation("Showing messenger view for authenticated user");
            var settings = _settingsService.LoadSettings();
            var handler = new AuthHandler(_tokenService, () => _authService, App.LoggerFactory.CreateLogger<AuthHandler>());
            handler.InnerHandler = new HttpClientHandler();
            var messengerHttpClient = new HttpClient(handler) { BaseAddress = new Uri(settings.ApiBaseAddress) };

            var vm = new MessengerViewModel(_authService, messengerHttpClient);

            MainGrid.Children.Clear();
            MainGrid.Children.Add(new MessengerView { DataContext = vm });
        }

        private void ShowAddContact()
        {
            if (!_authService.IsAuthenticated)
            {
                _logger.LogInformation("User not authenticated, cannot add contacts");
                return;
            }

            _logger.LogDebug("Showing add contact view");
            var settings = _settingsService.LoadSettings();
            var handler = new AuthHandler(_tokenService, () => _authService, App.LoggerFactory.CreateLogger<AuthHandler>());
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(settings.ApiBaseAddress) };

            var vm = new AddContactViewModel(_authService, httpClient, _logger);
            vm.OnContactAdded += () => {
                // Refresh the messenger view to show the new contact
                ShowMessenger();
            };
            vm.OnCloseRequested += ShowMessenger;

            MainGrid.Children.Clear();
            MainGrid.Children.Add(new AddContactView { DataContext = vm });
        }
    }
}

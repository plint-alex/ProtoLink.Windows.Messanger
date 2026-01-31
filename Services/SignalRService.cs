using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading.Tasks;

namespace ProtoLink.Windows.Messanger.Services
{
    public class SignalRService
    {
        private HubConnection? _connection;
        private readonly string _hubUrl;
        private readonly string _accessToken;
        private bool _isConnected;

        public event Action? MessageReceived;

        public SignalRService(string baseUrl, string accessToken)
        {
            _hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/commands";
            _accessToken = accessToken;
        }

        public async Task ConnectAsync()
        {
            try
            {
                _connection = new HubConnectionBuilder()
                    .WithUrl(_hubUrl, options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult(_accessToken);
                    })
                    .WithAutomaticReconnect()
                    .Build();

                _connection.On<string>("ReceiveCommand", async (message) =>
                {
                    // Handle incoming command messages
                    System.Diagnostics.Debug.WriteLine($"Received command: {message}");
                    MessageReceived?.Invoke();
                });

                _connection.Closed += async (error) =>
                {
                    System.Diagnostics.Debug.WriteLine($"SignalR connection closed: {error?.Message}");
                    _isConnected = false;
                    await Task.Delay(new Random().Next(0, 5) * 1000);
                    await ConnectAsync();
                };

                _connection.Reconnecting += (error) =>
                {
                    System.Diagnostics.Debug.WriteLine($"SignalR reconnecting: {error?.Message}");
                    _isConnected = false;
                    return Task.CompletedTask;
                };

                _connection.Reconnected += (connectionId) =>
                {
                    System.Diagnostics.Debug.WriteLine($"SignalR reconnected: {connectionId}");
                    _isConnected = true;
                    return Task.CompletedTask;
                };

                await _connection.StartAsync();
                _isConnected = true;
                System.Diagnostics.Debug.WriteLine("SignalR connected successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignalR connection failed: {ex.Message}");
                _isConnected = false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_connection != null)
            {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
                _connection = null;
                _isConnected = false;
                System.Diagnostics.Debug.WriteLine("SignalR disconnected");
            }
        }

        public bool IsConnected => _isConnected;
    }
}
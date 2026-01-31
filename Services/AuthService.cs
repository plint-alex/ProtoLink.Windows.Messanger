using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProtoLink.Windows.Messanger.Models;

namespace ProtoLink.Windows.Messanger.Services
{
    public interface IAuthService
    {
        Task<LoginResult> LoginAsync(string login, string password);
        Task<bool> RefreshTokenAsync();
        void Logout();
        bool IsAuthenticated { get; }
        TokenData? CurrentToken { get; }
    }

    public class AuthService : IAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly ITokenService _tokenService;
        private readonly ILogger<AuthService> _logger;
        private TokenData? _currentToken;

        public AuthService(HttpClient httpClient, ITokenService tokenService, ILogger<AuthService> logger)
        {
            _httpClient = httpClient;
            _tokenService = tokenService;
            _logger = logger;
            _currentToken = _tokenService.LoadToken();
            
            if (_currentToken != null)
            {
                _logger.LogInformation("Loaded existing token for user: {Login}", _currentToken.Login);
            }
        }

        public bool IsAuthenticated => _currentToken != null;
        public TokenData? CurrentToken => _currentToken;

        public async Task<LoginResult> LoginAsync(string login, string password)
        {
            _logger.LogInformation("Attempting login for user: {Login}", login);
            _logger.LogDebug("API Base Address: {BaseAddress}", _httpClient.BaseAddress);
            
            // Try different URL combinations to diagnose the issue
            var possibleUrls = new[]
            {
                $"{_httpClient.BaseAddress}api/Authentication/login",
                $"{_httpClient.BaseAddress}Authentication/login",
                $"{_httpClient.BaseAddress?.ToString().TrimEnd('/')}/api/Authentication/login",
                $"{_httpClient.BaseAddress?.ToString().TrimEnd('/')}/Authentication/login"
            };
            
            _logger.LogDebug("Possible request URLs:");
            foreach (var url in possibleUrls)
            {
                _logger.LogDebug("  - {Url}", url);
            }
            
            try
            {
                var contract = new LoginContract { Login = login, Password = password };
                _logger.LogDebug("Sending login request to: api/Authentication/login");
                _logger.LogDebug("Full URL will be: {FullUrl}", new Uri(_httpClient.BaseAddress!, "api/Authentication/login"));
                
                var response = await _httpClient.PostAsJsonAsync("api/Authentication/login", contract);
                
                // Log the actual request URL that was sent
                var actualRequestUrl = response.RequestMessage?.RequestUri?.ToString() ?? "Unknown";
                _logger.LogDebug("Actual request URL sent: {RequestUrl}", actualRequestUrl);
                
                _logger.LogDebug("Response received. Status: {StatusCode}, Reason: {ReasonPhrase}", 
                    response.StatusCode, response.ReasonPhrase);
                
                // Read response content for error details
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Response content length: {Length} bytes", responseContent?.Length ?? 0);
                if (!string.IsNullOrEmpty(responseContent))
                {
                    _logger.LogDebug("Response content: {Content}", responseContent);
                }
                else
                {
                    _logger.LogWarning("Response content is empty");
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("=== LOGIN FAILED ===");
                    _logger.LogError("User: {Login}", login);
                    _logger.LogError("HTTP Status: {StatusCode} {ReasonPhrase}", response.StatusCode, response.ReasonPhrase);
                    _logger.LogError("Request URL: {RequestUrl}", response.RequestMessage?.RequestUri);
                    _logger.LogError("Response content length: {Length} bytes", responseContent?.Length ?? 0);
                    
                    if (string.IsNullOrEmpty(responseContent))
                    {
                        _logger.LogError("Response body is EMPTY - This usually means:");
                        _logger.LogError("  1. The endpoint doesn't exist (404 Not Found)");
                        _logger.LogError("  2. The API base URL might be incorrect");
                        _logger.LogError("  3. The API server might not be running");
                        _logger.LogError("  4. There might be a routing/configuration issue on the server");
                    }
                    else
                    {
                        _logger.LogError("Response content: {Content}", responseContent);
                    }
                    
                    _logger.LogError("Response headers:");
                    foreach (var header in response.Headers)
                    {
                        _logger.LogError("  {Key}: {Value}", header.Key, string.Join(", ", header.Value));
                    }
                    
                    // Try to parse error from response if it's not empty
                    LoginResult? errorResult = null;
                    if (!string.IsNullOrEmpty(responseContent))
                    {
                        try
                        {
                            errorResult = Newtonsoft.Json.JsonConvert.DeserializeObject<LoginResult>(responseContent);
                            _logger.LogDebug("Parsed error result: {Error}", errorResult?.Error);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to parse error response as LoginResult. Response was: {Content}", responseContent);
                        }
                    }
                    
                    string errorMessage;
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        var baseUrlStr = _httpClient.BaseAddress?.ToString() ?? "";
                        var attemptedUrl = response.RequestMessage?.RequestUri?.ToString() ?? "Unknown";
                        
                        // Based on AuthenticationController.cs: [Route("api/[controller]")] + [HttpPost("login")]
                        // The correct endpoint is: api/Authentication/login
                        _logger.LogError("API endpoint not found (404).");
                        _logger.LogError("Attempted URL: {Url}", attemptedUrl);
                        _logger.LogError("Expected route (from API controller): api/Authentication/login");
                        _logger.LogError("Base URL configured: {BaseUrl}", baseUrlStr);
                        _logger.LogError("");
                        _logger.LogError("Possible issues:");
                        _logger.LogError("1. API is not deployed at {BaseUrl}", baseUrlStr);
                        _logger.LogError("2. API is deployed but not running");
                        _logger.LogError("3. URL rewriting or reverse proxy is misconfigured");
                        _logger.LogError("4. Base URL is incorrect - check settings");
                        _logger.LogError("");
                        _logger.LogError("Verify the API is accessible by checking:");
                        _logger.LogError("  - {BaseUrl}scalar/ (should show API documentation)", baseUrlStr.TrimEnd('/'));
                        _logger.LogError("  - {BaseUrl}api/version (if VersionController exists)", baseUrlStr.TrimEnd('/'));
                        
                        errorMessage = $"API endpoint not found (404).\n" +
                                     $"URL attempted: {attemptedUrl}\n" +
                                     $"Expected route: api/Authentication/login\n" +
                                     $"Base URL: {baseUrlStr}\n\n" +
                                     $"The API server is responding (IIS/ASP.NET detected) but the endpoint is not found.\n" +
                                     $"Please verify the API is deployed and running at the configured base URL.";
                    }
                    else if (string.IsNullOrEmpty(responseContent))
                    {
                        errorMessage = $"Server returned {response.StatusCode} with empty response. Check API configuration.";
                    }
                    else
                    {
                        errorMessage = errorResult?.Error ?? responseContent ?? $"Server error: {response.StatusCode}";
                    }
                    
                    _logger.LogError("Final error message: {ErrorMessage}", errorMessage);
                    _logger.LogError("=== END LOGIN ERROR ===");
                    
                    return new LoginResult { Error = errorMessage };
                }

                _logger.LogDebug("Parsing successful response...");
                LoginResult? result = null;
                if (!string.IsNullOrEmpty(responseContent))
                {
                    try
                    {
                        result = Newtonsoft.Json.JsonConvert.DeserializeObject<LoginResult>(responseContent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to parse successful response. Content: {Content}", responseContent);
                    }
                }
                
                if (result != null && string.IsNullOrEmpty(result.Error))
                {
                    _currentToken = new TokenData
                    {
                        AccessToken = result.AccessToken,
                        RefreshToken = result.RefreshToken,
                        ExpirationTime = result.ExpirationTime,
                        UserId = result.UserId,
                        Login = result.Login
                    };
                    _tokenService.SaveToken(_currentToken);
                    _logger.LogInformation("Login successful for user: {Login}, UserId: {UserId}", login, result.UserId);
                }
                else
                {
                    _logger.LogWarning("Login failed for user: {Login}, Error: {Error}", login, result?.Error ?? "Unknown error");
                }
                return result ?? new LoginResult { Error = "Unknown error" };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request exception during login for user: {Login}", login);
                _logger.LogError("Exception message: {Message}", ex.Message);
                _logger.LogError("Inner exception: {InnerException}", ex.InnerException?.Message ?? "None");
                return new LoginResult { Error = $"Network error: {ex.Message}" };
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Request timeout during login for user: {Login}", login);
                return new LoginResult { Error = "Request timeout. Please check your connection." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during login for user: {Login}", login);
                _logger.LogError("Exception type: {Type}", ex.GetType().Name);
                _logger.LogError("Exception message: {Message}", ex.Message);
                _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
                return new LoginResult { Error = $"Unexpected error: {ex.Message}" };
            }
        }

        public async Task<bool> RefreshTokenAsync()
        {
            if (_currentToken?.RefreshToken == null)
            {
                _logger.LogWarning("Token refresh attempted but no refresh token available");
                return false;
            }

            _logger.LogInformation("Attempting to refresh token for user: {Login}", _currentToken.Login);
            var contract = new RefreshTokenContract 
            { 
                AccessToken = _currentToken.AccessToken, 
                RefreshToken = _currentToken.RefreshToken.Value 
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/Authentication/refreshtoken", contract);
                
                _logger.LogDebug("Refresh token response: {StatusCode}", response.StatusCode);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("Refresh token response content: {Content}", responseContent);
                    
                    if (string.IsNullOrWhiteSpace(responseContent))
                    {
                        _logger.LogWarning("Token refresh returned empty response - server validation failed. User needs to re-login.");
                        _currentToken = null;
                        _tokenService.ClearToken();
                        return false;
                    }
                    
                    var result = Newtonsoft.Json.JsonConvert.DeserializeObject<RefreshTokenResult>(responseContent);
                    if (result != null && !string.IsNullOrEmpty(result.AccessToken))
                    {
                        _currentToken.AccessToken = result.AccessToken;
                        _currentToken.RefreshToken = result.RefreshToken;
                        _currentToken.ExpirationTime = result.ExpirationTime;
                        _tokenService.SaveToken(_currentToken);
                        _logger.LogInformation("Token refreshed successfully for user: {Login}", _currentToken.Login);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("Token refresh returned invalid result - user needs to re-login");
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Token refresh failed with status {StatusCode}: {Content}", 
                        response.StatusCode, errorContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during token refresh");
            }
            
            _logger.LogWarning("Token refresh failed for user: {Login} - clearing token, user needs to re-login", 
                _currentToken?.Login ?? "Unknown");
            _currentToken = null;
            _tokenService.ClearToken();
            return false;
        }

        public void Logout()
        {
            var login = _currentToken?.Login ?? "Unknown";
            _logger.LogInformation("Logging out user: {Login}", login);
            _currentToken = null;
            _tokenService.ClearToken();
        }
    }
}


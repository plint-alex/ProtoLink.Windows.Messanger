using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ProtoLink.Windows.Messanger.Services
{
    public class AuthHandler : DelegatingHandler
    {
        private readonly ITokenService _tokenService;
        private readonly Func<IAuthService> _authServiceFactory;
        private readonly ILogger<AuthHandler> _logger;

        public AuthHandler(ITokenService tokenService, Func<IAuthService> authServiceFactory, ILogger<AuthHandler> logger)
        {
            _tokenService = tokenService;
            _authServiceFactory = authServiceFactory;
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = _tokenService.LoadToken();
            if (token != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                _logger.LogDebug("Added authorization header for request to {Uri}", request.RequestUri);
            }
            else
            {
                _logger.LogDebug("No token available for request to {Uri}", request.RequestUri);
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogInformation("Received 401 Unauthorized, attempting token refresh");
                var authService = _authServiceFactory();
                if (await authService.RefreshTokenAsync())
                {
                    var newToken = _tokenService.LoadToken();
                    if (newToken != null)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken.AccessToken);
                        _logger.LogInformation("Token refreshed, retrying request to {Uri}", request.RequestUri);
                        return await base.SendAsync(request, cancellationToken);
                    }
                }
                else
                {
                    _logger.LogWarning("Token refresh failed, request will return 401");
                }
            }

            return response;
        }
    }
}


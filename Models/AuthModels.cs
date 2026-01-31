using System;

namespace ProtoLink.Windows.Messanger.Models
{
    public class TokenData
    {
        public string AccessToken { get; set; } = string.Empty;
        public Guid? RefreshToken { get; set; }
        public DateTime ExpirationTime { get; set; }
        public Guid UserId { get; set; }
        public string Login { get; set; } = string.Empty;
    }

    public class LoginContract
    {
        public string Login { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int GiveinPlaceId { get; set; }
    }

    public class LoginResult
    {
        public Guid UserId { get; set; }
        public string Login { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public Guid? RefreshToken { get; set; }
        public DateTime ExpirationTime { get; set; }
        public string? Error { get; set; }
    }

    public class RefreshTokenContract
    {
        public string AccessToken { get; set; } = string.Empty;
        public Guid RefreshToken { get; set; }
    }

    public class RefreshTokenResult
    {
        public string AccessToken { get; set; } = string.Empty;
        public Guid? RefreshToken { get; set; }
        public DateTime ExpirationTime { get; set; }
    }
}


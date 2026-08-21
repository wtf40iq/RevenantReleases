namespace RevenantAuthServer.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;

        /// <summary>Хеш пароля (PBKDF2-SHA256), base64</summary>
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>Соль пароля, base64</summary>
        public string PasswordSalt { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

        /// <summary>Активный refresh-токен (ротация при каждом использовании)</summary>
        public string? RefreshToken { get; set; }
        public DateTime RefreshTokenExpiry { get; set; }
    }
}

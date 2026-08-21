using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using RevenantAuthServer.Models;

namespace RevenantAuthServer.Services
{
    public class TokenService
    {
        private readonly SymmetricSecurityKey _key;
        private readonly string _issuer;
        private readonly string _audience;

        /// <summary>Access-токен живёт 15 минут</summary>
        private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

        /// <summary>Refresh-токен живёт 30 дней</summary>
        public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

        public TokenService(string secret, string issuer, string audience)
        {
            _key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret));
            _issuer = issuer;
            _audience = audience;
        }

        public string CreateAccessToken(User user)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim("username", user.Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: DateTime.UtcNow.Add(AccessTokenLifetime),
                signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>64 байта криптослучайности в base64 — перебор нереален</summary>
        public static string CreateRefreshToken()
            => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using RevenantAuthServer.Models;
using RevenantAuthServer.Services;
using Xunit;

namespace RevenantLauncher.Tests;

public class TokenServiceTests
{
    private const string Secret = "test-secret-key-that-is-longer-than-32-chars-12345";
    private const string Issuer = "revenant-auth-server";
    private const string Audience = "revenant-launcher";

    private static TokenService CreateService() => new(Secret, Issuer, Audience);

    [Fact]
    public void CreateAccessToken_ContainsUserClaims()
    {
        var token = CreateService().CreateAccessToken(new User { Id = 42, Username = "player1" });
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Contains(jwt.Audiences, a => a == Audience);
        Assert.Equal("42", jwt.Subject);
        Assert.Equal("player1", jwt.Claims.First(c => c.Type == "username").Value);
    }

    [Fact]
    public void CreateAccessToken_ExpiresInAbout15Minutes()
    {
        var token = CreateService().CreateAccessToken(new User { Id = 1, Username = "p" });
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var expected = DateTime.UtcNow.AddMinutes(15);
        var diff = Math.Abs((jwt.ValidTo - expected).TotalSeconds);
        Assert.True(diff < 60, $"exp не в пределах минуты от 15 мин (diff={diff:F1}с)");
    }

    [Fact]
    public void CreateAccessToken_JtiIsUniquePerToken()
    {
        var svc = CreateService();
        var user = new User { Id = 1, Username = "p" };
        var jti1 = new JwtSecurityTokenHandler().ReadJwtToken(svc.CreateAccessToken(user)).Id;
        var jti2 = new JwtSecurityTokenHandler().ReadJwtToken(svc.CreateAccessToken(user)).Id;
        Assert.NotEqual(jti1, jti2);
    }

    [Fact]
    public void CreateRefreshToken_IsUniqueAndCryptoStrong()
    {
        var t1 = TokenService.CreateRefreshToken();
        var t2 = TokenService.CreateRefreshToken();
        Assert.NotEqual(t1, t2);
        Assert.True(Convert.FromBase64String(t1).Length >= 64, "refresh-токен короче 64 байт");
    }
}

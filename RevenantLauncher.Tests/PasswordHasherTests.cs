using RevenantAuthServer.Services;
using Xunit;

namespace RevenantLauncher.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void HashAndVerify_CorrectPassword_ReturnsTrue()
    {
        var (hash, salt) = PasswordHasher.Hash("my-secret-pass");
        Assert.True(PasswordHasher.Verify("my-secret-pass", hash, salt));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var (hash, salt) = PasswordHasher.Hash("correct-password");
        Assert.False(PasswordHasher.Verify("wrong-password", hash, salt));
    }

    [Fact]
    public void Hash_SamePassword_ProducesDifferentSaltAndHash()
    {
        var (h1, s1) = PasswordHasher.Hash("password123");
        var (h2, s2) = PasswordHasher.Hash("password123");
        Assert.NotEqual(s1, s2);
        Assert.NotEqual(h1, h2);
        // Соль 16 байт, ключ 32 байта
        Assert.Equal(16, Convert.FromBase64String(s1).Length);
        Assert.Equal(32, Convert.FromBase64String(h1).Length);
    }

    [Fact]
    public void Verify_TamperedHash_ReturnsFalse()
    {
        var (hash, salt) = PasswordHasher.Hash("password123");
        var bytes = Convert.FromBase64String(hash);
        bytes[0] ^= 0xFF;
        Assert.False(PasswordHasher.Verify("password123", Convert.ToBase64String(bytes), salt));
    }

    [Fact]
    public void Verify_InvalidBase64_ReturnsFalseWithoutThrowing()
    {
        Assert.False(PasswordHasher.Verify("x", "!!!not-base64!!!", "AAAA"));
        Assert.False(PasswordHasher.Verify("x", "AAAA", "!!!not-base64!!!"));
    }
}

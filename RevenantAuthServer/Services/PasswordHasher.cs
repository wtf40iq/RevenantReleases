using System.Security.Cryptography;

namespace RevenantAuthServer.Services
{
    /// <summary>
    /// Хеширование паролей через PBKDF2-SHA256 (встроено в .NET, без внешних зависимостей).
    /// 100 000 итераций, 16-байтная соль, 32-байтный ключ.
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 100_000;
        private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

        public static (string hash, string salt) Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
            return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
        }

        public static bool Verify(string password, string hashBase64, string saltBase64)
        {
            try
            {
                var salt = Convert.FromBase64String(saltBase64);
                var expected = Convert.FromBase64String(hashBase64);
                var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
                // FixedTimeEquals — защита от timing-атак
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using Flit.Modules.Security.Domain.Auth;
using Konscious.Security.Cryptography;

namespace Flit.Infrastructure.Security;

public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;

    // Hash precalculado una sola vez para verificaciones de relleno en tiempo constante.
    private static readonly Lazy<string> DummyHashValue = new(() =>
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashPassword("argon2-timing-equalizer", salt);
        return $"argon2id|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(hash)}";
    });

    public string DummyHash => DummyHashValue.Value;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashPassword(password, salt);
        return $"argon2id|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('|');
        if (parts.Length != 3 || !string.Equals(parts[0], "argon2id", StringComparison.Ordinal))
            return false;

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = HashPassword(password, salt);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] HashPassword(string password, byte[] salt)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = 4,
            MemorySize = 65536,
            Iterations = 3,
        };
        return argon2.GetBytes(HashSize);
    }
}

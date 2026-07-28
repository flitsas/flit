using System.Security.Cryptography;
using System.Text;
using Flit.Ict.Domain.Abstractions;
using Konscious.Security.Cryptography;

namespace Flit.Ict.Infrastructure.Security;

/// <summary>
/// Hashing Argon2id de contraseñas de clientes de integración. Formato serializado:
/// <c>argon2id$v=1$m,t,p$salt_b64$hash_b64</c>. La verificación es en tiempo constante y hace
/// trabajo dummy cuando el hash es nulo/vacío para no filtrar la existencia del usuario.
/// </summary>
public sealed class Argon2PasswordHasher : IIctPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemoryKb = 19456; // 19 MiB
    private const int Iterations = 2;
    private const int Parallelism = 1;

    private static readonly byte[] DummySalt = new byte[SaltSize];

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt);
        return $"argon2id$v=1${MemoryKb},{Iterations},{Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string? passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash))
        {
            // Trabajo dummy para tiempo constante (no filtra existencia del usuario).
            _ = Derive(password, DummySalt);
            return false;
        }

        var parts = passwordHash.Split('$');
        if (parts.Length != 5)
        {
            _ = Derive(password, DummySalt);
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            _ = Derive(password, DummySalt);
            return false;
        }

        var actual = Derive(password, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = MemoryKb,
            Iterations = Iterations,
            DegreeOfParallelism = Parallelism,
        };
        return argon2.GetBytes(HashSize);
    }
}

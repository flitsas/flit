using System.Security.Cryptography;
using System.Text;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Security;

/// <summary>
/// Genera tokens de recuperación de 256 bits (base64url) y los hashea con SHA-256 para
/// almacenamiento. El hash es determinista (sin sal) porque el token crudo ya es de alta
/// entropía, lo que permite buscarlo por <c>token_hash</c>.
/// </summary>
public sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    private const int TokenBytes = 32;

    public GeneratedToken Generate()
    {
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        return new GeneratedToken(raw, HashToken(raw));
    }

    public string HashToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

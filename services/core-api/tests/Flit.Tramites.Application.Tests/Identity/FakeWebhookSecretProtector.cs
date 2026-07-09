using System.Security.Cryptography;
using Flit.Tramites.Application.Identity;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// Protector de secretos de prueba: simula el cifrado con un prefijo reversible <c>prot::</c>. Permite
/// verificar que el secreto se persiste "cifrado" (distinto del claro) y se descifra al round-trip. Con
/// <see cref="ThrowOnUnprotect"/> simula el keyring roto (Unprotect lanza <see cref="CryptographicException"/>).
/// </summary>
public sealed class FakeWebhookSecretProtector : IWebhookSecretProtector
{
    private const string Prefix = "prot::";

    /// <summary>Si es true, <see cref="Unprotect"/> lanza (simula "key not found in key ring").</summary>
    public bool ThrowOnUnprotect { get; set; }

    public string Protect(string plaintextSecret) => Prefix + plaintextSecret;

    public string Unprotect(string protectedSecret)
    {
        if (ThrowOnUnprotect)
            throw new CryptographicException("The key was not found in the key ring.");
        return protectedSecret.StartsWith(Prefix, StringComparison.Ordinal) ? protectedSecret[Prefix.Length..] : protectedSecret;
    }
}

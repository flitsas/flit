using Flit.Tramites.Application.Identity;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// Protector de secretos de prueba: simula el cifrado con un prefijo reversible <c>prot::</c>. Permite
/// verificar que el secreto se persiste "cifrado" (distinto del claro) y se descifra al round-trip.
/// </summary>
public sealed class FakeWebhookSecretProtector : IWebhookSecretProtector
{
    private const string Prefix = "prot::";

    public string Protect(string plaintextSecret) => Prefix + plaintextSecret;

    public string Unprotect(string protectedSecret) =>
        protectedSecret.StartsWith(Prefix, StringComparison.Ordinal) ? protectedSecret[Prefix.Length..] : protectedSecret;
}

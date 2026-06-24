using Flit.Tramites.Application.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace Flit.Infrastructure.Kyverum;

/// <summary>
/// <see cref="IWebhookSecretProtector"/> sobre la Data Protection API (HU #10233). Cifra el secreto HMAC
/// del webhook con un propósito dedicado antes de persistirlo en <c>webhook_secret_encrypted</c>; lo
/// descifra solo en memoria para verificar la firma del callback. El secreto nunca se guarda ni loguea
/// en claro.
/// </summary>
internal sealed class DataProtectionWebhookSecretProtector : IWebhookSecretProtector
{
    private const string Purpose = "Flit.Kyverum.WebhookSecret.v1";

    private readonly IDataProtector _protector;

    public DataProtectionWebhookSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintextSecret) => _protector.Protect(plaintextSecret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}

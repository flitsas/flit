namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Cifra/descifra el secreto HMAC del webhook de Kyverum. La implementación vive en Infraestructura
/// sobre la Data Protection API (<c>DataProtectionWebhookSecretProtector</c>). El secreto NUNCA se
/// persiste ni se loguea en claro: en BD solo se guarda <c>Protect(secret)</c>, y se descifra en
/// memoria únicamente para verificar la firma del webhook entrante.
/// </summary>
public interface IWebhookSecretProtector
{
    /// <summary>Cifra el secreto en claro para persistirlo (<c>webhook_secret_encrypted</c>).</summary>
    string Protect(string plaintextSecret);

    /// <summary>Descifra el secreto persistido para verificar la firma HMAC del webhook.</summary>
    string Unprotect(string protectedSecret);
}

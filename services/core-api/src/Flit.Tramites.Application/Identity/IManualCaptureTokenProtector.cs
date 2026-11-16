namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Cifra/descifra el token del enlace de captura manual (HU #14305, Feature #14304, Épica #14271) para guardarlo en
/// <c>manual_capture_token_encrypted</c> y poder entregar el enlace al Super Admin sin regenerarlo. La implementación vive en
/// Infraestructura sobre la Data Protection API con un propósito propio. El token NUNCA se persiste ni se loguea en claro.
/// </summary>
public interface IManualCaptureTokenProtector
{
    /// <summary>Cifra el token en claro para persistirlo.</summary>
    string Protect(string plaintextToken);

    /// <summary>Descifra el token persistido. Devuelve <c>null</c> si no se puede descifrar (llaves rotadas, dato corrupto):
    /// nunca lanza ni expone el motivo.</summary>
    string? TryUnprotect(string protectedToken);
}

using Flit.Tramites.Application.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace Flit.Infrastructure.Notifications.Identity;

/// <summary>
/// <see cref="IManualCaptureTokenProtector"/> sobre la Data Protection API (HU #14305). Propósito propio, distinto del secreto del
/// webhook de Kyverum: un blob de un uso no se descifra con el otro. El token nunca se guarda ni se loguea en claro.
/// </summary>
internal sealed class DataProtectionManualCaptureTokenProtector : IManualCaptureTokenProtector
{
    private const string Purpose = "Flit.Identity.ManualCaptureToken.v1";

    private readonly IDataProtector _protector;

    public DataProtectionManualCaptureTokenProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintextToken) => _protector.Protect(plaintextToken);

    // Llaves rotadas o dato corrupto lanzan CryptographicException: se traga sin detalle (ni mensaje ni valor) y el caso de uso
    // responde «enlace_no_disponible».
    public string? TryUnprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

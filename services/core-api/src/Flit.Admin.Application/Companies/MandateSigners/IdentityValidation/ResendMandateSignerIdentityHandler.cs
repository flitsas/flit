using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.IdentityValidation;

public enum ResendMandateSignerIdentityOutcome
{
    /// <summary>Se lanzó una validación nueva y el enlace salió al correo del mandatario.</summary>
    Sent,

    /// <summary>El proveedor falló de forma transitoria: la validación nueva quedó encolada para reintento.</summary>
    Queued,

    /// <summary>No existe, está eliminado o no es de ese organismo (404).</summary>
    NotFound,

    /// <summary>Persona jurídica, Formato en blanco o forma de firma baúl: no hay validación que enviar (409).</summary>
    NoRequiereValidacion,

    /// <summary>Con biometría el correo es obligatorio y la ficha no lo tiene (422, campo <c>email</c>).</summary>
    CorreoRequerido,

    /// <summary>El proveedor rechazó el envío de forma definitiva (502).</summary>
    ProveedorError,
}

public sealed record ResendMandateSignerIdentityResult(ResendMandateSignerIdentityOutcome Outcome, Guid? ValidationId = null);

/// <summary>
/// HU #13246 (Feature #13245, Épica #13090) — «Reenviar validación» de un mandatario Persona natural con forma de firma
/// biometría, desde la compañía y desde el hub OT. Lanza una validación propia NUEVA (la anterior en vuelo se cierra y, por
/// ser la más reciente la que cuenta, la aprobada anterior deja de contar hasta que la nueva se apruebe). Ruta nueva con el
/// permiso de gestión de mandatarios: las tres rutas <c>identity/send|resend|link</c> siguen respondiendo 410.
/// Dos reenvíos simultáneos dejan una sola validación activa.
/// </summary>
public sealed class ResendMandateSignerIdentityHandler
{
    private readonly IMandateSignerReader _reader;
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IMandateSignerIdentityLauncher _launcher;

    public ResendMandateSignerIdentityHandler(
        IMandateSignerReader reader,
        ITransitOfficeOperationalStatusReader otStatus,
        IMandateSignerIdentityLauncher launcher)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _otStatus = otStatus ?? throw new ArgumentNullException(nameof(otStatus));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <param name="transitOfficeId">Ruta del hub OT: el mandatario debe pertenecer a ese organismo. Nulo en la ruta de la compañía.</param>
    /// <param name="companyTenantId">Ruta de la compañía: tenant donde se registra la validación si el mandatario está vinculado a ella.</param>
    public async Task<ResendMandateSignerIdentityResult> HandleAsync(
        Guid mandateSignerId,
        Guid? transitOfficeId,
        Guid? companyTenantId,
        CancellationToken cancellationToken = default)
    {
        // GetByIdAsync ya excluye a los eliminados (baja lógica): un eliminado responde 404.
        var signer = await _reader.GetByIdAsync(mandateSignerId, cancellationToken).ConfigureAwait(false);
        if (signer is null
            || (transitOfficeId is { } office
                && signer.TransitOfficeId != office
                && !signer.TransitOfficeIds.Contains(office)))
        {
            return new ResendMandateSignerIdentityResult(ResendMandateSignerIdentityOutcome.NotFound);
        }

        if (!MandateSignerIdentityLaunch.RequiresValidation(
                signer.SignerModel, MandateSignerIdentityLaunch.EffectiveMethod(signer)))
        {
            return new ResendMandateSignerIdentityResult(ResendMandateSignerIdentityOutcome.NoRequiereValidacion);
        }

        if (string.IsNullOrWhiteSpace(signer.Email) || string.IsNullOrWhiteSpace(signer.DocumentNumber))
        {
            return new ResendMandateSignerIdentityResult(ResendMandateSignerIdentityOutcome.CorreoRequerido);
        }

        var tenantId = companyTenantId is { } c && signer.CompanyTenantIds.Contains(c)
            ? c
            : signer.CompanyTenantIds.Count > 0
                ? signer.CompanyTenantIds[0]
                // Respaldo para un registro sin compañías vinculadas: el tenant propio del organismo.
                : (await _otStatus.GetByIdAsync(signer.TransitOfficeId, cancellationToken).ConfigureAwait(false))
                    ?.TenantId ?? Guid.Empty;
        if (tenantId == Guid.Empty)
        {
            return new ResendMandateSignerIdentityResult(ResendMandateSignerIdentityOutcome.NotFound);
        }

        var result = await _launcher
            .LaunchAsync(
                new MandateSignerIdentityLaunchRequest(
                    signer.Id, tenantId, signer.DocumentType, signer.DocumentNumber.Trim(), signer.FullName,
                    signer.Email.Trim()),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            MandateSignerIdentityLaunchOutcome.Sent or MandateSignerIdentityLaunchOutcome.AlreadyInFlight =>
                new ResendMandateSignerIdentityResult(ResendMandateSignerIdentityOutcome.Sent, result.ValidationId),
            MandateSignerIdentityLaunchOutcome.Queued =>
                new ResendMandateSignerIdentityResult(ResendMandateSignerIdentityOutcome.Queued, result.ValidationId),
            _ => new ResendMandateSignerIdentityResult(ResendMandateSignerIdentityOutcome.ProveedorError),
        };
    }
}

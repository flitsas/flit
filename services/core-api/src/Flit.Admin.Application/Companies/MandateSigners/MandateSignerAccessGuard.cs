using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners;

/// <summary>Desenlace de la comprobación de permisos sobre un mandatario (HU #13134).</summary>
public enum MandateSignerAccess
{
    /// <summary>El actor puede escribir sobre el mandatario.</summary>
    Allowed,

    /// <summary>No existe (o está eliminado, o no es de esta compañía): 404 sin revelar nada más.</summary>
    NotFound,

    /// <summary>El actor no tiene rol de escritura sobre mandatarios: 403 <c>mandatario_sin_permiso</c>.</summary>
    Forbidden,

    /// <summary>Configurado por el organismo de tránsito y el actor es de la compañía: 403 con candado.</summary>
    LockedByOtOrigin,
}

/// <summary>
/// HU #13134 — comprobación ÚNICA de permisos de escritura sobre un mandatario desde las rutas de la compañía
/// (propias y de hijas de la red). Aplica <see cref="MandateSignerOriginRules"/>: un mandatario configurado por el
/// organismo (o el Super Admin) no lo modifica el Admin de Compañía; uno configurado por la compañía sí. El
/// aislamiento por tenant (<c>CompanyOwnTenantFilter</c>) y la cabeza de red (<c>GroupHeadTenantAccess</c>) los
/// resuelve la capa API antes de llegar aquí.
/// </summary>
public sealed class MandateSignerAccessGuard
{
    private readonly IMandateSignerReader _reader;

    public MandateSignerAccessGuard(IMandateSignerReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <summary>
    /// Permiso de <paramref name="actor"/> para escribir sobre <paramref name="mandateSignerId"/> visto desde la
    /// compañía <paramref name="companyTenantId"/>. El rol sin permiso (<see cref="MandateSignerActorKind.None"/>)
    /// se rechaza ANTES de consultar, para no revelar si el mandatario existe.
    /// </summary>
    public async Task<MandateSignerAccess> CheckCompanyWriteAsync(
        MandateSignerActorKind actor,
        Guid companyTenantId,
        Guid mandateSignerId,
        CancellationToken cancellationToken = default)
    {
        if (actor == MandateSignerActorKind.None)
        {
            return MandateSignerAccess.Forbidden;
        }

        var origin = await _reader
            .GetOriginForCompanyAsync(mandateSignerId, companyTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (origin is null)
        {
            return MandateSignerAccess.NotFound;
        }

        return MandateSignerOriginRules.CanModify(actor, origin)
            ? MandateSignerAccess.Allowed
            : MandateSignerAccess.LockedByOtOrigin;
    }
}

using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="IRnmcRequirementPolicy"/> (HU #10602): resuelve el OT destino
/// del trámite (el elegido en el FUR o, si aún no hay, el único grant vigente de la empresa) y lee
/// <c>admin.ot_requirements.requires_rnmc</c> de ese OT. La lectura de requisitos es abierta por RLS
/// (config operativa); la escritura sigue aislada por tenant. Clon de <c>IdentityValidationPolicy</c>
/// salvo el default seguro: RNMC nace desactivado (<c>false</c>).
/// </summary>
internal sealed class RnmcRequirementPolicy : IRnmcRequirementPolicy
{
    private readonly FlitDbContext _context;
    private readonly ITransitGrantRepository _grants;

    public RnmcRequirementPolicy(FlitDbContext context, ITransitGrantRepository grants)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
    }

    public async Task<bool> IsRnmcRequiredAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var officeId = await ResolveOfficeAsync(tenantId, transitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (officeId is not { } office)
        {
            // Sin OT resoluble: no se exige RNMC (default seguro; no bloquea trámites existentes).
            return false;
        }

        var requires = await _context.OtRequirements
            .AsNoTracking()
            .Where(r => r.TransitOfficeId == office)
            .Select(r => (bool?)r.RequiresRnmc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Sin fila configurada → default: no se exige RNMC.
        return requires ?? false;
    }

    private async Task<Guid?> ResolveOfficeAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken)
    {
        if (transitOfficeId is { } explicitOffice && explicitOffice != Guid.Empty)
        {
            return explicitOffice;
        }

        // Aún sin OT en el FUR: si la empresa tiene exactamente un grant vigente, ese es el destino.
        var enabledOffices = await _grants
            .ListEnabledOfficeIdsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        return enabledOffices.Count == 1 ? enabledOffices[0] : null;
    }
}

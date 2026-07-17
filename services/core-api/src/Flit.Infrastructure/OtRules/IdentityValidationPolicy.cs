using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="IIdentityValidationPolicy"/> (HU #10548): resuelve el OT
/// destino del trámite (el elegido en el FUR o, si aún no hay, el único grant vigente de la
/// empresa) y lee <c>admin.ot_requirements.identity_validation_enabled</c> de ese OT. La lectura de
/// requisitos es abierta por RLS (config operativa); la escritura sigue aislada por tenant.
/// </summary>
internal sealed class IdentityValidationPolicy : IIdentityValidationPolicy
{
    private readonly FlitDbContext _context;
    private readonly ITransitGrantRepository _grants;

    public IdentityValidationPolicy(FlitDbContext context, ITransitGrantRepository grants)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
    }

    public async Task<bool> IsIdentityValidationRequiredAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var officeId = await TransitOfficeDestinationResolver
            .ResolveAsync(_grants, tenantId, transitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (officeId is not { } office)
        {
            // Sin OT resoluble: no se puede afirmar que esté deshabilitada → se exige (AC2).
            return true;
        }

        var enabled = await _context.OtRequirements
            .AsNoTracking()
            .Where(r => r.TransitOfficeId == office)
            .Select(r => (bool?)r.IdentityValidationEnabled)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Sin fila configurada → default seguro: se exige identidad (AC2).
        return enabled ?? true;
    }
}

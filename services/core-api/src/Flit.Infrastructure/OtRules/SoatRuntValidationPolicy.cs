using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Lee <c>validate_soat_with_runt</c> de las políticas operativas de la compañía.
/// <c>true</c> = opción activa: permite continuar aunque el SOAT no esté vigente.
/// Sin fila de políticas (o <c>false</c>) la opción está apagada y un SOAT no vigente bloquea.
/// </summary>
internal sealed class SoatRuntValidationPolicy(FlitDbContext db) : ISoatRuntValidationPolicy
{
    public async Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantOperationalPolicies
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => p.ValidateSoatWithRunt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
}

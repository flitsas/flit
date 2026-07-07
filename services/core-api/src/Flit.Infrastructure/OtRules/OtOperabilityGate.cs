using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="IOtOperabilityGate"/> (HU #10518): resuelve si un
/// organismo de tránsito está operativo a nivel plataforma leyendo
/// <c>catalogs.transit_offices</c> + <c>admin.transit_office_profiles</c> +
/// <c>identity.tenants</c> (misma lógica de join que
/// <c>DbTransitOfficeOperationalStatusReader</c>).
///
/// La radicación corre bajo el contexto RLS de la EMPRESA que radica, pero el perfil/tenant
/// del OT pertenecen a OTRO tenant; por eso la lectura desactiva RLS solo para esta
/// transacción (<c>SET LOCAL row_security = off</c>), igual que el listado operativo del
/// SuperAdmin. En proveedor no relacional (InMemory, tests) se lee directo.
/// </summary>
internal sealed class OtOperabilityGate : IOtOperabilityGate
{
    private readonly FlitDbContext _context;

    public OtOperabilityGate(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<bool> IsOperableAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            () => EvaluateAsync(transitOfficeId, cancellationToken),
            cancellationToken);

    private async Task<bool> EvaluateAsync(Guid transitOfficeId, CancellationToken cancellationToken)
    {
        // a) La oficina del catálogo debe estar activa.
        var officeActive = await _context.TransitOffices
            .AsNoTracking()
            .AnyAsync(o => o.Id == transitOfficeId && o.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (!officeActive)
        {
            return false;
        }

        // b) Debe existir el perfil OT (una oficina = a lo sumo un tenant OT).
        var tenantId = await _context.TransitOfficeProfiles
            .AsNoTracking()
            .Where(p => p.TransitOfficeId == transitOfficeId)
            .Select(p => (Guid?)p.TenantId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (tenantId is null)
        {
            return false;
        }

        // c) El tenant OT debe existir y estar activo (is_active = true).
        return await _context.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Id == tenantId.Value && t.IsActive, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<T> ExecuteCrossTenantReadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return await action().ConfigureAwait(false);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "SET LOCAL row_security = off",
                    cancellationToken).ConfigureAwait(false);

                var result = await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }
}

using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura EF Core del estado operativo de los organismos de tránsito (RF01):
/// <c>catalogs.transit_offices</c> (activos) LEFT JOIN <c>admin.transit_office_profiles</c>
/// + <c>identity.tenants</c>. El perfil OT tiene RLS por tenant y el SuperAdmin es
/// cross-tenant por definición, así que la lectura corre con
/// <c>SET LOCAL row_security = off</c> (mismo patrón que
/// <see cref="TransitOfficeTenantWriteRepository.ListAsync"/>).
///
/// Se materializan las tres fuentes por separado y se combinan en memoria (una fila por
/// oficina) para funcionar igual en PostgreSQL y en el proveedor InMemory de los tests.
/// Hay a lo sumo un perfil por oficina (índice único
/// <c>transit_office_profiles.transit_office_id</c>).
/// </summary>
internal sealed class DbTransitOfficeOperationalStatusReader : ITransitOfficeOperationalStatusReader
{
    private readonly FlitDbContext _context;

    public DbTransitOfficeOperationalStatusReader(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<TransitOfficeOperationalStatusItem>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return await ExecuteCrossTenantReadAsync(
            async () =>
            {
                var offices = await _context.TransitOffices
                    .AsNoTracking()
                    .Where(o => o.IsActive)
                    .OrderBy(o => o.Name)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var profiles = await _context.TransitOfficeProfiles
                    .AsNoTracking()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var profileByOffice = profiles
                    .GroupBy(p => p.TransitOfficeId)
                    .ToDictionary(g => g.Key, g => g.First());

                var tenantIds = profiles.Select(p => p.TenantId).Distinct().ToList();
                var tenants = await _context.Tenants
                    .AsNoTracking()
                    .Where(t => tenantIds.Contains(t.Id))
                    .ToDictionaryAsync(t => t.Id, cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<TransitOfficeOperationalStatusItem> items =
                [
                    .. offices.Select(office =>
                    {
                        var profile = profileByOffice.GetValueOrDefault(office.Id);
                        var tenant = profile is null ? null : tenants.GetValueOrDefault(profile.TenantId);
                        var hasTenant = profile is not null && tenant is not null;

                        return new TransitOfficeOperationalStatusItem
                        {
                            Id = office.Id,
                            Code = office.Code,
                            Name = office.Name,
                            DepartmentCode = office.DepartmentCode,
                            HasTenant = hasTenant,
                            TenantId = hasTenant ? tenant!.Id : null,
                            EstadoActivo = hasTenant ? tenant!.IsActive : null,
                            OperationMode = hasTenant ? profile!.OperationMode : null,
                        };
                    }),
                ];

                return items;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<TransitOfficeOperationalStatusItem?> GetByIdAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            () => ProjectOfficeAsync(transitOfficeId, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Proyecta el estado operativo de una sola oficina (mismo join catálogo + perfil +
    /// tenant que <see cref="ListAsync"/>, acotado por id). <c>null</c> si la oficina no
    /// existe o no está activa en el catálogo.
    /// </summary>
    private async Task<TransitOfficeOperationalStatusItem?> ProjectOfficeAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken)
    {
        var office = await _context.TransitOffices
            .AsNoTracking()
            .Where(o => o.Id == transitOfficeId && o.IsActive)
            .Select(o => new { o.Id, o.Code, o.Name, o.DepartmentCode })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (office is null)
        {
            return null;
        }

        var profile = await _context.TransitOfficeProfiles
            .AsNoTracking()
            .Where(p => p.TransitOfficeId == transitOfficeId)
            .Select(p => new { p.TenantId, p.OperationMode })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var tenant = profile is null
            ? null
            : await _context.Tenants
                .AsNoTracking()
                .Where(t => t.Id == profile.TenantId)
                .Select(t => new { t.Id, t.IsActive })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        var hasTenant = profile is not null && tenant is not null;

        return new TransitOfficeOperationalStatusItem
        {
            Id = office.Id,
            Code = office.Code,
            Name = office.Name,
            DepartmentCode = office.DepartmentCode,
            HasTenant = hasTenant,
            TenantId = hasTenant ? tenant!.Id : null,
            EstadoActivo = hasTenant ? tenant!.IsActive : null,
            OperationMode = hasTenant ? profile!.OperationMode : null,
        };
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

using Flit.Admin.Domain.Companies.MandateSigners;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13178 — lectura cross-tenant de las compañías gestoras de FLIT sobre <c>identity.tenants</c>, con
/// proyección mínima (id, razón social, NIT, alta, activo). Excluye los organismos de tránsito (tienen
/// <c>transit_office_profiles</c>) y el tenant de la plataforma (<c>FLIT</c>), salvo en
/// <see cref="ListOwnerCandidatesByIdsAsync"/> (un mandatario sí puede pertenecer a la plataforma, HU #13182b). Solo la consumen el OT y el Super
/// Admin (lo impone el endpoint) y la validación del guardado; aparte de <see cref="CrossTenantRead"/> no toca la
/// visibilidad de la bandeja de trámites (<c>OtVisibleCompanies</c>, Bug #12912).
/// </summary>
internal sealed class ManagingCompanyDirectory : IManagingCompanyDirectory
{
    /// <summary>Tipo de tenant de la propia plataforma: no es una compañía gestora.</summary>
    private const string PlatformTenantType = "FLIT";

    private readonly FlitDbContext _context;

    public ManagingCompanyDirectory(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<IReadOnlyList<ManagingCompanyRow>> ListAsync(CancellationToken cancellationToken = default) =>
        CrossTenantRead.ExecuteAsync<IReadOnlyList<ManagingCompanyRow>>(
            _context,
            async () => await _context.Tenants.AsNoTracking()
                .Where(t => t.TenantType != PlatformTenantType
                    && !_context.TransitOfficeProfiles.Any(p => p.TenantId == t.Id))
                .Select(t => new ManagingCompanyRow(t.Id, t.LegalName, t.TaxId, t.CreatedAt, t.IsActive))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
            cancellationToken);

    public Task<IReadOnlyList<ManagingCompanyRow>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ManagingCompanyRow>>([]);
        }

        var wanted = ids.Distinct().ToList();
        return CrossTenantRead.ExecuteAsync<IReadOnlyList<ManagingCompanyRow>>(
            _context,
            async () => await _context.Tenants.AsNoTracking()
                .Where(t => wanted.Contains(t.Id)
                    && t.TenantType != PlatformTenantType
                    && !_context.TransitOfficeProfiles.Any(p => p.TenantId == t.Id))
                .Select(t => new ManagingCompanyRow(t.Id, t.LegalName, t.TaxId, t.CreatedAt, t.IsActive))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
            cancellationToken);
    }

    public Task<IReadOnlyList<ManagingCompanyRow>> ListOwnerCandidatesByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ManagingCompanyRow>>([]);
        }

        // HU #13182b — el tenant de la plataforma (FLIT) SÍ puede tener mandatarios: solo se excluyen los organismos.
        var wanted = ids.Distinct().ToList();
        return CrossTenantRead.ExecuteAsync<IReadOnlyList<ManagingCompanyRow>>(
            _context,
            async () => await _context.Tenants.AsNoTracking()
                .Where(t => wanted.Contains(t.Id)
                    && !_context.TransitOfficeProfiles.Any(p => p.TenantId == t.Id))
                .Select(t => new ManagingCompanyRow(t.Id, t.LegalName, t.TaxId, t.CreatedAt, t.IsActive))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
            cancellationToken);
    }
}

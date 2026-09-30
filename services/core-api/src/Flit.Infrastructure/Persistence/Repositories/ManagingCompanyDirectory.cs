using Flit.Admin.Domain.Companies.MandateSigners;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13178 — lectura cross-tenant de las compañías gestoras de FLIT sobre <c>identity.tenants</c>, con
/// proyección mínima (id, razón social, NIT, alta, activo). Excluye los organismos de tránsito (tienen
/// <c>transit_office_profiles</c>) y el tenant de la plataforma (<c>FLIT</c>). Solo la consumen el OT y el Super
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
}

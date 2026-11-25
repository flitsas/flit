using Flit.Infrastructure.Persistence;
using Flit.Modules.Platform.Application.TenantProducts;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Platform;

/// <summary>
/// HU #14694 (D-EMAIL; platform.v1 1.1.0) — precondición para encender un producto con aprovisionamiento: la compañía
/// tiene al menos un AdminCompany activo (el mismo criterio de <see cref="DatosAltaProveedorStore.AdminCompanyActivos"/>).
/// Sin él, el PUT responde 409 <c>PROVISIONING_PREREQUISITE_MISSING</c>.
/// </summary>
internal sealed class PrecondicionAdminCompanyActivo(IIdentityDb db) : IPrecondicionActivacion
{
    private readonly IIdentityDb _db = db;

    public Task<bool> CumpleAsync(Guid tenantId, string productCode, CancellationToken cancellationToken = default) =>
        DatosAltaProveedorStore.AdminCompanyActivos(_db, tenantId).AnyAsync(cancellationToken);
}

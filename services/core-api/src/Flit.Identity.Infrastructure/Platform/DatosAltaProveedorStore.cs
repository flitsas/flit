using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Platform.Application.Companies;
using Flit.Modules.Security.Application.Products;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Platform;

/// <summary>
/// HU #14694 (plan §1.3, decisión P1) — datos crudos de la compañía para el alta en el proveedor: razón social
/// (<c>identity.tenants.legal_name</c>), NIT tal cual, NIT de cada ancestro (DA-8) y correo del AdminCompany activo más
/// antiguo (D-EMAIL). La normalización del NIT la hace <c>ObtenerDatosAltaProveedorHandler</c>.
/// </summary>
internal sealed class DatosAltaProveedorStore(IIdentityDb db) : IDatosAltaProveedorStore
{
    /// <summary>Tope de niveles al subir por la jerarquía (defensa ante un ciclo).</summary>
    private const int MaxNiveles = 16;

    private readonly IIdentityDb _db = db;

    /// <summary>
    /// Asignaciones vigentes de AdminCompany en la compañía cuyo usuario está activo (<c>status = active</c> y sin borrar).
    /// Un invitado pendiente o una asignación cerrada no cuentan.
    /// </summary>
    internal static IQueryable<UserRoleAssignment> AdminCompanyActivos(IIdentityDb db, Guid tenantId) =>
        db.UserRoleAssignments.AsNoTracking()
            .Where(a => a.TenantId == tenantId
                        && a.DeletedAt == null
                        && a.Role.Code == ProductRoleCodes.AdminCompany
                        && a.Role.TargetEntityType == "COMPANY"
                        && a.Role.DeletedAt == null
                        && a.User.Status == "active"
                        && a.User.DeletedAt == null);

    public async Task<DatosEmpresaParaAlta?> ObtenerAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var empresa = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.LegalName, t.TaxId, t.IsActive, t.ParentTenantId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (empresa is null)
            return null;

        var ancestros = new List<string?>();
        var vistos = new HashSet<Guid> { tenantId };
        var padre = empresa.ParentTenantId;
        while (padre is { } id && vistos.Add(id) && ancestros.Count < MaxNiveles)
        {
            var fila = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new { t.TaxId, t.ParentTenantId })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (fila is null)
                break;
            ancestros.Add(fila.TaxId);
            padre = fila.ParentTenantId;
        }

        // «El más antiguo» (D-EMAIL) = la asignación de AdminCompany vigente más vieja; empates por alta del usuario.
        var correo = await AdminCompanyActivos(_db, tenantId)
            .OrderBy(a => a.AssignedAt)
            .ThenBy(a => a.User.CreatedAt)
            .ThenBy(a => a.UserId)
            .Select(a => a.User.Email)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new DatosEmpresaParaAlta(empresa.LegalName, empresa.TaxId, empresa.IsActive, ancestros, correo);
    }
}

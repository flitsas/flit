namespace Flit.Modules.Security.Application.Products;

/// <summary>
/// Códigos de rol que la FLIT Suite trata de forma especial (HU #12964, decisión D1 del inventario B-01).
/// </summary>
/// <remarks>
/// Un usuario tiene un rol por producto en cada empresa. Mientras el hub no permita asignar un rol por
/// producto (B-12), <see cref="AdminTramites"/> es el espejo de <see cref="AdminCompany"/>: el disparador
/// <c>security.trg_ura_mirror_admin_tramites</c> (DDL 120) lo crea al asignar AdminCompany y lo cierra al
/// quitarlo. Por eso las pantallas actuales no lo ofrecen ni lo muestran como rol principal. Desde HU #12967 lo mismo
/// vale para el admin de cada producto (<see cref="IsProductAdmin"/>).
/// </remarks>
public static class ProductRoleCodes
{
    /// <summary>Administrador de la empresa en la plataforma (hub).</summary>
    public const string AdminCompany = "AdminCompany";

    /// <summary>Administrador de Trámites: los permisos de Trámites que antes tenía AdminCompany.</summary>
    public const string AdminTramites = "admin_tramites";

    /// <summary>
    /// <c>true</c> si el código es el admin de un producto (<c>admin_&lt;código&gt;</c>: admin_tramites,
    /// admin_comparendos, admin_diagnostico…). Todos son espejo de AdminCompany (DDL 120 y 126): no se ofrecen ni
    /// se muestran como rol principal.
    /// </summary>
    public static bool IsProductAdmin(string? roleCode) =>
        roleCode is not null
        && roleCode.StartsWith("admin_", StringComparison.Ordinal)
        && ProductCodes.All.Contains(roleCode["admin_".Length..])
        && roleCode != "admin_" + ProductCodes.Plataforma;
}

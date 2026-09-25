namespace Flit.Modules.Security.Application.Products;

/// <summary>
/// Códigos de rol que la FLIT Suite trata de forma especial (HU #12964, decisión D1 del inventario B-01).
/// </summary>
/// <remarks>
/// Un usuario tiene un rol por producto en cada empresa. Mientras el hub no permita asignar un rol por
/// producto (B-12), <see cref="AdminTramites"/> es el espejo de <see cref="AdminCompany"/>: el disparador
/// <c>security.trg_ura_mirror_admin_tramites</c> (DDL 120) lo crea al asignar AdminCompany y lo cierra al
/// quitarlo. Por eso las pantallas actuales no lo ofrecen ni lo muestran como rol principal.
/// </remarks>
public static class ProductRoleCodes
{
    /// <summary>Administrador de la empresa en la plataforma (hub).</summary>
    public const string AdminCompany = "AdminCompany";

    /// <summary>Administrador de Trámites: los permisos de Trámites que antes tenía AdminCompany.</summary>
    public const string AdminTramites = "admin_tramites";
}

namespace Flit.Admin.Domain.Companies.Create;

/// <summary>
/// Catálogo de tipos de compañía (<c>identity.tenants.tenant_type</c>) admitidos
/// al dar de alta una compañía B2B. La validación de negocio vive en la capa de
/// aplicación (mensajes 422 por campo) y, además, la BD la enforce con el CHECK
/// <c>ck_tenants_tenant_type</c> (migración <c>RestrictTenantTypeCatalog</c>), que
/// impide insertar/actualizar tipos fuera de este catálogo incluso por SQL manual.
/// Mantener ambos lados en sync.
/// </summary>
public static class CompanyTenantTypes
{
    public const string Renting = "RENTING";
    public const string Concesionario = "CONCESIONARIO";
    public const string Flit = "FLIT";

    /// <summary>Conjunto de valores válidos (comparación sensible a mayúsculas, ya normalizado).</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Renting,
        Concesionario,
        Flit,
    };

    public static bool IsValid(string? value) =>
        value is not null && All.Contains(value);
}

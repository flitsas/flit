namespace Flit.Admin.Domain.Companies.Create;

/// <summary>
/// Catálogo de tipos de compañía (<c>identity.tenants.tenant_type</c>) admitidos
/// al dar de alta una compañía B2B. La columna es <c>varchar(20)</c> sin CHECK en
/// BD, por lo que la validación del valor vive en la capa de aplicación.
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

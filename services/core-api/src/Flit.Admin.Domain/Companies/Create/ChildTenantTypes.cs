namespace Flit.Admin.Domain.Companies.Create;

/// <summary>
/// HU #12345 AC1 — tipos de compañía admitidos al dar de alta o editar un cliente hijo.
/// Solo <see cref="Renting"/> y <see cref="Concesionario"/>; la BD los enforce con el trigger
/// de jerarquía y el CHECK de catálogo.
/// </summary>
public static class ChildTenantTypes
{
    public const string Renting = CompanyTenantTypes.Renting;

    public const string Concesionario = CompanyTenantTypes.Concesionario;

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Renting,
        Concesionario,
    };

    public const string DisplayList = "RENTING o CONCESIONARIO";

    public static bool IsValid(string? value) =>
        value is not null && All.Contains(value);

    /// <summary>
    /// Tipo propuesto por defecto según la cabeza de grupo (AC1 / AC9).
    /// CONCESION → CONCESIONARIO; MARCA_BLANCA → RENTING.
    /// </summary>
    public static string DefaultForHeadType(string headTenantType) =>
        string.Equals(headTenantType, HeadTenantTypes.MarcaBlanca, StringComparison.Ordinal)
            ? Renting
            : Concesionario;
}

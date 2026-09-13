using Flit.Queries.Domain.Tenancy;

namespace Flit.Admin.Domain.Companies.Create;

/// <summary>
/// Catálogo de tipos de compañía (<c>identity.tenants.tenant_type</c>) admitidos
/// al dar de alta o editar una compañía B2B. La validación de negocio vive en la capa de
/// aplicación (mensajes 422 por campo) y, además, la BD la enforce con el CHECK
/// <c>ck_tenants_tenant_type</c> (migración <c>RestrictTenantTypeCatalog</c>, ampliado por
/// HU #12406), que impide insertar/actualizar tipos fuera de este catálogo incluso por SQL
/// manual. Mantener ambos lados en sync.
/// <para>
/// HU #12406: los dos tipos de <b>cabeza de grupo</b> (<see cref="Concesion"/>,
/// <see cref="MarcaBlanca"/>) viven en este mismo catálogo — la clase de la cabeza ES su tipo
/// de compañía — y se agrupan en <see cref="HeadTenantTypes"/>. <c>is_group_parent</c> vale
/// exactamente <see cref="HeadTenantTypes.IsHead"/> (CHECK <c>ck_tenants_group_parent_by_type</c>).
/// </para>
/// </summary>
public static class CompanyTenantTypes
{
    public const string Renting = "RENTING";
    public const string Concesionario = "CONCESIONARIO";
    public const string Flit = "FLIT";

    /// <summary>Cabeza de grupo de clase Concesión (HU #12406).</summary>
    public const string Concesion = HeadTenantTypes.Concesion;

    /// <summary>Cabeza de grupo de clase Marca Blanca (HU #12406).</summary>
    public const string MarcaBlanca = HeadTenantTypes.MarcaBlanca;

    /// <summary>Conjunto de valores válidos (comparación sensible a mayúsculas, ya normalizado).</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Renting,
        Concesionario,
        Flit,
        Concesion,
        MarcaBlanca,
    };

    /// <summary>Texto del catálogo para mensajes 422.</summary>
    public const string DisplayList = "RENTING, CONCESIONARIO, FLIT, CONCESION o MARCA_BLANCA";

    public static bool IsValid(string? value) =>
        value is not null && All.Contains(value);
}

/// <summary>
/// HU #12406 — mapeo ÚNICO de los tipos de compañía que son cabeza de grupo. Los literales son los
/// mismos que persiste <c>identity.tenants.tenant_type</c>, que admite el CHECK
/// <c>ck_tenants_group_parent_by_type</c> y que <see cref="GroupKindCodes"/> traduce a
/// <see cref="GroupKind"/> para el alcance de lectura (<c>TenantScope.Group</c>).
/// </summary>
public static class HeadTenantTypes
{
    public const string Concesion = GroupKindCodes.Concesion;

    public const string MarcaBlanca = GroupKindCodes.MarcaBlanca;

    /// <summary>Los dos tipos de cabeza (sensible a mayúsculas, ya normalizado).</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Concesion,
        MarcaBlanca,
    };

    /// <summary><c>true</c> si <paramref name="tenantType"/> es un tipo de cabeza de grupo; es el valor que debe llevar <c>is_group_parent</c>.</summary>
    public static bool IsHead(string? tenantType) =>
        tenantType is not null && All.Contains(tenantType);
}

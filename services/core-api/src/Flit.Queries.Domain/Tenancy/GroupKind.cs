namespace Flit.Queries.Domain.Tenancy;

/// <summary>
/// Clase de una cabeza de grupo (HU #12406, Feature #12254, Épica #12235). Dato ESTRUCTURAL: es el
/// propio <c>identity.tenants.tenant_type</c> de la cabeza (<c>CONCESION</c> | <c>MARCA_BLANCA</c>);
/// de él dependen la política de organismos, la marca y el acceso por dominio. Se lee siempre de la
/// base de datos (nunca de cabecera, cuerpo ni token) y viaja en <see cref="TenantScope.GroupKind"/>
/// junto al conjunto de lectura de la cabeza.
/// </summary>
public enum GroupKind
{
    /// <summary>Concesión: la cabeza consolida la operación de sus compañías hijas.</summary>
    Concesion = 1,

    /// <summary>Marca blanca: la cabeza presta su marca y su dominio a sus compañías hijas.</summary>
    MarcaBlanca = 2,
}

/// <summary>
/// Códigos persistidos de <see cref="GroupKind"/> — los valores de <c>tenant_type</c> que son de
/// cabeza (CHECK <c>ck_tenants_group_parent_by_type</c>). Cualquier otro tipo (RENTING,
/// CONCESIONARIO, FLIT, <c>null</c>…) no es una clase de cabeza.
/// </summary>
public static class GroupKindCodes
{
    public const string Concesion = "CONCESION";

    public const string MarcaBlanca = "MARCA_BLANCA";

    /// <summary>Código persistido de la clase.</summary>
    public static string ToCode(GroupKind kind) => kind switch
    {
        GroupKind.Concesion => Concesion,
        GroupKind.MarcaBlanca => MarcaBlanca,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Clase de cabeza de grupo desconocida."),
    };

    /// <summary>
    /// <c>true</c> si <paramref name="code"/> es exactamente uno de los códigos admitidos
    /// (sensible a mayúsculas, sin recortar: lo que hay en la base es lo que vale).
    /// </summary>
    public static bool TryParse(string? code, out GroupKind kind)
    {
        switch (code)
        {
            case Concesion:
                kind = GroupKind.Concesion;
                return true;
            case MarcaBlanca:
                kind = GroupKind.MarcaBlanca;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}

namespace Flit.Api.Authorization;

/// <summary>
/// Perfiles funcionales de la plataforma (context/contexto-perfiles.md). Son una CLASIFICACIÓN
/// derivada, no una entidad ni un claim nuevo: se calculan a partir del rol de sistema y del tipo
/// de tenant al que pertenece la asignación. Existen para que el frontend no tenga que inferirlos.
/// </summary>
/// <remarks>
/// No confundir con <c>security.roles.target_entity_type</c>, que solo admite
/// <c>COMPANY</c> | <c>TRANSIT_OFFICE</c> por el CHECK <c>ck_roles_target_entity_type</c>
/// (HU #10505). El perfil FLIT no tiene <c>target_entity_type</c> propio: el rol SuperAdmin
/// vive con <c>COMPANY</c> y es transversal a la plataforma.
/// </remarks>
public static class UserProfiles
{
    /// <summary>Equipo interno de FLIT (proveedor de la plataforma) — rol de sistema SuperAdmin.</summary>
    public const string Flit = "FLIT";

    /// <summary>Funcionarios de un Organismo de Tránsito.</summary>
    public const string TransitOffice = "OT";

    /// <summary>Empresas cliente que radican trámites (gestores, concesionarios…).</summary>
    public const string Manager = "GESTOR";

    /// <summary>Los tres perfiles válidos, para validar entradas de la API.</summary>
    public static readonly string[] All = [Flit, TransitOffice, Manager];

    public static bool IsValid(string? profile) =>
        profile is not null && All.Contains(profile, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Valores de <c>security.roles.target_entity_type</c> — el dominio cerrado por el CHECK
/// <c>ck_roles_target_entity_type</c> en Postgres (HU #10505). Centralizados porque hasta ahora
/// se repetían como literales en endpoints, repositorios y seeders.
/// </summary>
public static class TenantTypes
{
    public const string Company = "COMPANY";

    public const string TransitOffice = "TRANSIT_OFFICE";
}

namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// HU #13134 (ADR-0061, Feature #13115) — quién actúa sobre un mandatario. Lo traduce la capa API desde el
/// principal; las capas internas lo reciben como dato, igual que <c>OtCompanyVisibility</c>.
/// </summary>
public enum MandateSignerActorKind
{
    /// <summary>Sin rol de gestión de mandatarios (Gestor/Radicador, usuario OT sin <c>ot_admin</c>, desconocido).</summary>
    None = 0,

    /// <summary>Super Admin de la plataforma: gestiona cualquier mandatario.</summary>
    SuperAdmin = 1,

    /// <summary>Admin del organismo de tránsito (<c>ot_admin</c>): gestiona los de su organismo.</summary>
    OtAdmin = 2,

    /// <summary>Admin de Compañía (o su cabeza de red): gestiona solo los configurados por la compañía.</summary>
    CompanyAdmin = 3,
}

/// <summary>
/// HU #13134 — REGLA ÚNICA de permisos por origen del mandatario: un mandatario configurado por el organismo de
/// tránsito (origen <c>organismo</c>, que incluye al Super Admin) solo lo modifican el Admin OT y el Super Admin; uno
/// configurado por la compañía (origen <c>compania</c>) lo modifican además el Admin de Compañía (su tenant o su red
/// si es cabeza). Reutilizada por las rutas del hub OT, las de la compañía (propias y de hijas) y por las banderas
/// <c>puedeEditar</c> / <c>puedeEliminar</c> de los listados, para que la UI no duplique la regla.
/// </summary>
public static class MandateSignerOriginRules
{
    public const string Organismo = "organismo";
    public const string Compania = "compania";

    /// <summary>Código de error (403) cuando la compañía intenta tocar un mandatario configurado por el organismo.</summary>
    public const string LockedErrorCode = "mandatario_configurado_por_organismo";

    public const string LockedMessage =
        "Este mandatario fue configurado por el organismo de tránsito; solo el organismo o el Super Admin pueden modificarlo.";

    /// <summary>Código de error (403) para los roles sin permiso de escritura sobre mandatarios.</summary>
    public const string ForbiddenErrorCode = "mandatario_sin_permiso";

    public const string ForbiddenMessage = "Su perfil no puede gestionar mandatarios.";

    /// <summary>Grupo de origen de un <c>configured_by_scope</c>: <c>compania</c> o <c>organismo</c> (incluye <c>super_admin</c>).</summary>
    public static string GroupOf(string? configuredByScope) =>
        string.Equals(configuredByScope, Compania, StringComparison.OrdinalIgnoreCase) ? Compania : Organismo;

    /// <summary>
    /// Origen del mandatario a partir de los orígenes de sus vínculos: basta uno configurado por el organismo (o el
    /// Super Admin) para que el mandatario cuente como del organismo y quede bajo candado para la compañía. Sin
    /// vínculos se asume del organismo (así nacen los mandatarios sin compañías).
    /// </summary>
    public static string OriginOf(IEnumerable<string> configuredByScopes)
    {
        ArgumentNullException.ThrowIfNull(configuredByScopes);
        var groups = configuredByScopes.Select(GroupOf).ToList();
        return groups.Count > 0 && groups.All(g => g == Compania) ? Compania : Organismo;
    }

    /// <summary>¿<paramref name="actor"/> puede editar, inactivar, reactivar o eliminar un mandatario de ese origen?</summary>
    public static bool CanModify(MandateSignerActorKind actor, string origin) => actor switch
    {
        MandateSignerActorKind.SuperAdmin => true,
        MandateSignerActorKind.OtAdmin => true,
        MandateSignerActorKind.CompanyAdmin => string.Equals(origin, Compania, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>Rol del actor tal como lo guarda la bitácora (HU #13138).</summary>
    public static string AuditRole(MandateSignerActorKind actor) => actor switch
    {
        MandateSignerActorKind.SuperAdmin => "super_admin",
        MandateSignerActorKind.OtAdmin => "admin_ot",
        MandateSignerActorKind.CompanyAdmin => "admin_compania",
        _ => "desconocido",
    };

    /// <summary>Módulo desde el que actúa (HU #13138): Admin Compañía, Admin OT o Plataforma.</summary>
    public static string AuditModule(MandateSignerActorKind actor) => actor switch
    {
        MandateSignerActorKind.SuperAdmin => "plataforma",
        MandateSignerActorKind.OtAdmin => "ot",
        MandateSignerActorKind.CompanyAdmin => "compania",
        _ => "desconocido",
    };
}

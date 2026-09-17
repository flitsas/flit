namespace Flit.Modules.Security.Domain.UiPreferences;

/// <summary>
/// Lista blanca de <c>scope</c> aceptados para preferencias de UI (HU base de tres criterios de
/// negocio: selección de columnas visibles en tablas de trámites). Vive en el dominio (no en la
/// base de datos, ver DDL) para que agregar un scope nuevo sea solo un cambio de código, sin
/// migración — pero sigue siendo una regla de negocio, no un detalle de infraestructura.
/// </summary>
public static class UiPreferenceScopes
{
    /// <summary>Columnas visibles en la tabla de trámites de Operación.</summary>
    public const string TramitesColumns = "tramites.columns";

    /// <summary>Columnas visibles en la tabla de trámites de clientes del hub OT.</summary>
    public const string OtProceduresColumns = "ot.procedures.columns";

    /// <summary>
    /// HU #12358 (Feature #12257) — alcance elegido en el listado de trámites por una cabeza de grupo
    /// (propio | red | un cliente hijo), para que el selector de la HU #12363 lo persista. Es solo la
    /// preferencia visual: el alcance efectivo lo decide el servidor (TenantScope), nunca este valor.
    /// </summary>
    public const string TramitesScope = "tramites.scope";

    public static readonly IReadOnlyCollection<string> All = [TramitesColumns, OtProceduresColumns, TramitesScope];

    public static bool IsValid(string? scope) =>
        !string.IsNullOrWhiteSpace(scope) && All.Contains(scope);
}

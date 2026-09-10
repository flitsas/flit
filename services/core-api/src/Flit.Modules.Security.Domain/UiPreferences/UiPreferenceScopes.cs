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

    public static readonly IReadOnlyCollection<string> All = [TramitesColumns, OtProceduresColumns];

    public static bool IsValid(string? scope) =>
        !string.IsNullOrWhiteSpace(scope) && All.Contains(scope);
}

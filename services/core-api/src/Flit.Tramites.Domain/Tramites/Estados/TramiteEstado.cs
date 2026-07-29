namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Estados de NEGOCIO del ciclo de vida del trámite (N 03, RF01). Son los valores que se
/// persisten en <c>tramites.procedure_instances.status</c> (en español, snake_case-safe) y
/// los que expone la API. Reemplazan al vocabulario draft/submitted/... (ADR-0022).
/// </summary>
public static class TramiteEstado
{
    public const string Borrador = "borrador";
    public const string Anulado = "anulado";
    public const string Preparado = "preparado";
    public const string Entregado = "entregado";
    public const string Aprobado = "aprobado";
    public const string Rechazado = "rechazado";

    /// <summary>
    /// LEGACY — ya no es un estado de negocio activo. La subsanación vive como flag
    /// <c>subsanacion_activa</c> sobre <see cref="Rechazado"/>. Se conserva la constante para
    /// leer historial / filas migradas pendientes. No forma parte de <see cref="Todos"/>.
    /// </summary>
    public const string Subsanacion = "subsanacion";

    // Feature #10587 (matrícula inicial): la ruta de placa NO introduce estados de trámite. El
    // progreso de placa vive en un sub-estado interno ortogonal al status global (que permanece en
    // 'entregado'); ver <see cref="PlateFlowStatus"/> y <see cref="PlateFlowStateMachine"/> (HU #10785).

    /// <summary>Todos los estados válidos (para validación de entrada y checks DDL).</summary>
    public static readonly IReadOnlyList<string> Todos =
        [Borrador, Anulado, Preparado, Entregado, Aprobado, Rechazado];

    /// <summary>Estados FINALES (RF04): sin transiciones posteriores ni edición de datos.</summary>
    public static readonly IReadOnlyList<string> Finales = [Aprobado, Anulado];

    /// <summary>
    /// Estados "en proceso" (CF-01, HU #10876): activan el bloqueo de duplicidad de trámite por
    /// familia. Los estados finales (<see cref="Aprobado"/>, <see cref="Rechazado"/>,
    /// <see cref="Anulado"/>) NO cuentan por sí solos. Un <see cref="Rechazado"/> con
    /// <c>subsanacion_activa</c> SÍ cuenta (ver <see cref="EstaEnProceso"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> EstadosEnProceso = [Borrador, Preparado, Entregado];

    /// <summary>¿<paramref name="estado"/> es un estado de negocio conocido?</summary>
    public static bool EsValido(string? estado) =>
        estado is not null && Todos.Contains(estado, StringComparer.Ordinal);

    /// <summary>¿<paramref name="estado"/> es final (RF04)? Aprobado y Anulado son inmutables.</summary>
    public static bool EsFinal(string? estado) =>
        estado is Aprobado or Anulado;

    /// <summary>
    /// ¿El trámite está "en proceso" para duplicidad (CF-01)? Incluye el legado
    /// <see cref="Subsanacion"/> y <see cref="Rechazado"/> con flag de subsanación activa.
    /// </summary>
    public static bool EstaEnProceso(string? estado, bool subsanacionActiva = false) =>
        estado is not null
        && (EstadosEnProceso.Contains(estado, StringComparer.OrdinalIgnoreCase)
            || string.Equals(estado, Subsanacion, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(estado, Rechazado, StringComparison.OrdinalIgnoreCase) && subsanacionActiva));

    /// <summary>
    /// ¿Se pueden editar datos del expediente (campos, actores, adjuntos, etc.)?
    /// Editable en <see cref="Borrador"/>, en <see cref="Rechazado"/> con subsanación activa,
    /// o (legacy) en <see cref="Subsanacion"/>.
    /// </summary>
    public static bool PermiteEdicionDatos(string? status, bool subsanacionActiva = false) =>
        string.Equals(status, Borrador, StringComparison.OrdinalIgnoreCase)
        || (string.Equals(status, Rechazado, StringComparison.OrdinalIgnoreCase) && subsanacionActiva)
        || string.Equals(status, Subsanacion, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿La re-radicación selectiva (gates por diff de snapshot) aplica a esta transición a entregado?
    /// </summary>
    public static bool EsReRadicacionSubsanacion(string? from, bool subsanacionActiva) =>
        string.Equals(from, Subsanacion, StringComparison.OrdinalIgnoreCase)
        || (string.Equals(from, Rechazado, StringComparison.OrdinalIgnoreCase) && subsanacionActiva);
}

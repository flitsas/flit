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
    /// HU #10870 — subsanación: reabre la edición de un trámite entregado/rechazado SIN volver a
    /// borrador, conservando el historial. Es "en proceso" (no libera la llave de duplicidad, ver
    /// <see cref="EstadosEnProceso"/>) y editable (ver
    /// <c>Flit.Tramites.Application.UseCases.ProcedureInstances.PatchFieldValuesHandler</c> y el
    /// trigger <c>tramites.trg_field_value_immutable</c>).
    /// </summary>
    public const string Subsanacion = "subsanacion";

    // Feature #10587 (matrícula inicial): la ruta de placa NO introduce estados de trámite. El
    // progreso de placa vive en un sub-estado interno ortogonal al status global (que permanece en
    // 'entregado'); ver <see cref="PlateFlowStatus"/> y <see cref="PlateFlowStateMachine"/> (HU #10785).

    /// <summary>Todos los estados válidos (para validación de entrada y checks DDL).</summary>
    public static readonly IReadOnlyList<string> Todos =
        [Borrador, Anulado, Preparado, Entregado, Aprobado, Rechazado, Subsanacion];

    /// <summary>Estados FINALES (RF04): sin transiciones posteriores ni edición de datos.</summary>
    public static readonly IReadOnlyList<string> Finales = [Aprobado, Anulado];

    /// <summary>
    /// Estados "en proceso" (CF-01, HU #10876): activan el bloqueo de duplicidad de trámite por
    /// familia (Matrícula Inicial → VIN, Traspaso → placa, ver
    /// <c>Flit.Tramites.Domain.Tramites.Services.DuplicateActiveProcedurePolicy</c>). Los estados finales de este enum
    /// (<see cref="Aprobado"/>, <see cref="Rechazado"/>, <see cref="Anulado"/>) NO cuentan como "en
    /// proceso" y LIBERAN la llave. <see cref="Subsanacion"/> (HU #10870) SÍ cuenta: el trámite sigue
    /// activo mientras se corrige para re-radicarse.
    /// </summary>
    public static readonly IReadOnlyList<string> EstadosEnProceso = [Borrador, Preparado, Entregado, Subsanacion];

    /// <summary>¿<paramref name="estado"/> es un estado de negocio conocido?</summary>
    public static bool EsValido(string? estado) =>
        estado is not null && Todos.Contains(estado, StringComparer.Ordinal);

    /// <summary>¿<paramref name="estado"/> es final (RF04)? Aprobado y Anulado son inmutables.</summary>
    public static bool EsFinal(string? estado) =>
        estado is Aprobado or Anulado;
}

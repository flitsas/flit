namespace Flit.Modules.Quipux.Domain.LogQx;

/// <summary>
/// Cabecera de una radicación para la pantalla de trazabilidad (HU #11787): identifica el trámite y
/// su estado, y sitúa esta radicación entre las que el trámite acumuló.
/// </summary>
public sealed class QuipuxTrazabilidadRadicacion
{
    public Guid Id { get; init; }

    public Guid ProcedureInstanceId { get; init; }

    public string ReferenceNumber { get; init; } = string.Empty;

    public string? Plate { get; init; }

    public string ProcedureTypeName { get; init; } = string.Empty;

    public string ClientTenantName { get; init; } = string.Empty;

    public string TransitOfficeName { get; init; } = string.Empty;

    public string? DivipoCode { get; init; }

    /// <summary>Nombre del documento en Quipux — la llave de correlación (ADR-0051, D4).</summary>
    public string DocumentoQx { get; init; } = string.Empty;

    /// <summary>Estado crudo de la radicación: <c>pendiente|registrado|aprobado|rechazado|fallido</c>.</summary>
    public string Status { get; init; } = string.Empty;

    public int Attempts { get; init; }

    public int PollCount { get; init; }

    public int? QxRegisterCode { get; init; }

    public int? QxProcedureCode { get; init; }

    public string? RejectionReason { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? RegisteredAt { get; init; }

    public DateTimeOffset? LastPolledAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Posición de esta radicación entre las del trámite, de la más antigua a la más nueva (1..N).</summary>
    public int Intento { get; init; }

    /// <summary>Cuántas radicaciones acumuló el trámite. Mayor que 1 ⇒ la interfaz muestra la tira.</summary>
    public int TotalIntentos { get; init; }

    /// <summary>Las radicaciones hermanas, para poder saltar entre intentos sin salir de la pantalla.</summary>
    public IReadOnlyList<QuipuxRadicacionHermana> Hermanas { get; init; } = [];
}

/// <summary>Una radicación del mismo trámite, para la tira de intentos.</summary>
public sealed record QuipuxRadicacionHermana(
    Guid Id,
    int Intento,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>
/// Un evento reducido a lo que la agrupación necesita. NO trae el <c>detail</c> completo: para
/// decidir si un evento es un latido bastan la etapa, el resultado y el estado del trámite, y
/// cargar el jsonb entero de miles de sondeos para descartarlos sería trabajo tirado.
/// </summary>
public sealed record QuipuxEventoResumen(
    string Stage,
    string Outcome,
    DateTimeOffset OccurredAt,
    long? DurationMs,
    int? Codigo,
    int? EstadoTramite,
    string? Mensaje,
    Guid? CorrelationId);

/// <summary>Filtro del log completo. Los dos interruptores se combinan.</summary>
/// <param name="OcultarSinNovedad">
/// Activo por defecto en la interfaz: descarta los sondeos que no cambiaron nada. Es lo que
/// convierte 1.065 filas en las 5 que dicen algo.
/// </param>
/// <param name="SoloErrores">Deja únicamente los eventos cuyo resultado no es correcto.</param>
public sealed record QuipuxEventosQuery(
    Guid SubmissionId,
    bool OcultarSinNovedad,
    bool SoloErrores,
    int Page,
    int PageSize);

/// <summary>Un evento del log completo, con su <c>detail</c> sanitizado tal cual se persistió.</summary>
public sealed record QuipuxEventoDetallado(
    string Stage,
    string Outcome,
    string? Detail,
    DateTimeOffset OccurredAt,
    Guid? CorrelationId);

/// <summary>
/// Página del log completo. <paramref name="OcultosSinNovedad"/> es cuántos sondeos se dejaron
/// fuera: sin ese número, una lista de 5 filas sobre una radicación de 1.065 eventos parece una
/// pérdida de datos.
/// </summary>
public sealed record QuipuxEventosPage(
    IReadOnlyList<QuipuxEventoDetallado> Eventos,
    int TotalCount,
    int OcultosSinNovedad,
    int TotalEventos);

/// <summary>
/// Lectura de la trazabilidad de una radicación (HU #11787). Solo consulta y cross-tenant, por el
/// mismo motivo que <see cref="IQuipuxLogRepository"/>.
/// </summary>
public interface IQuipuxTrazabilidadRepository
{
    /// <summary>Cabecera de la radicación con sus hermanas; <c>null</c> si el id no existe.</summary>
    Task<QuipuxTrazabilidadRadicacion?> GetRadicacionAsync(
        Guid submissionId, CancellationToken cancellationToken = default);

    /// <summary>Eventos reducidos, del más viejo al más nuevo — el orden que exige la agrupación.</summary>
    Task<IReadOnlyList<QuipuxEventoResumen>> ListEventosParaHitosAsync(
        Guid submissionId, CancellationToken cancellationToken = default);

    /// <summary>Log completo, filtrado y paginado EN SERVIDOR.</summary>
    Task<QuipuxEventosPage> ListEventosAsync(
        QuipuxEventosQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Regla de agrupación del sondeo (ADR-0051, D2). Un evento es un LATIDO cuando es una consulta de
/// estado que salió bien y no movió nada; todo lo demás es un hito y se muestra individualmente.
/// </summary>
/// <remarks>
/// Vive en el dominio y no en el repositorio ni en la interfaz porque es la regla que decide qué se
/// le oculta al usuario: tiene que estar en un solo sitio, ser legible y poder probarse sola.
/// <para>El estado del trámite <c>1</c> significa «sin cambios» en el contrato de Quipux; ausente
/// significa que la respuesta no lo traía, que para el caso es lo mismo. Un <c>2</c> (aprobado) o un
/// <c>3</c> (rechazado) SÍ mueven el trámite y por eso rompen el bloque.</para>
/// </remarks>
public static class QuipuxSondeo
{
    public const int EstadoSinCambios = 1;

    public static bool EsLatido(QuipuxEventoResumen e)
    {
        ArgumentNullException.ThrowIfNull(e);

        return e.Stage.StartsWith("consulta", StringComparison.Ordinal)
            && string.Equals(e.Outcome, "ok", StringComparison.Ordinal)
            && e.EstadoTramite is null or EstadoSinCambios;
    }
}

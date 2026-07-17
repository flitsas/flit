namespace Flit.Modules.Quipux.Application.UseCases.RegistrarDocumento;

/// <summary>
/// Orden de radicar en Quipux una submission ya encolada. La emite <c>QuipuxRegisterProcessor</c>
/// tras reclamar la fila con <c>FOR UPDATE SKIP LOCKED</c> (que ya incrementó <c>attempts</c>).
/// </summary>
public sealed class RegistrarDocumentoQuipuxCommand
{
    /// <summary>Submission a radicar. Debe estar en <c>pendiente</c>.</summary>
    public required Guid SubmissionId { get; init; }

    /// <summary>Correlaciona los eventos de bitácora de este ciclo del worker (y con <c>admin.ot_api_call_logs</c>).</summary>
    public Guid? CorrelationId { get; init; }
}

/// <summary>Desenlace de una radicación.</summary>
public enum RegistrarDocumentoQuipuxStatus
{
    /// <summary>Quipux respondió 81. Submission <c>registrado</c> y trámite <c>entregado</c>.</summary>
    Registrado,

    /// <summary>
    /// La consulta previa reveló que Quipux YA lo tenía radicado (81) de un intento cuyo POST expiró.
    /// No se volvió a radicar. Es el desenlace que evita el duplicado de FLIT 1.0.
    /// </summary>
    YaRegistradoEnQuipux,

    /// <summary>Sin configuración completa (o <c>enabled = false</c>): no-op silencioso, modo de regresión.</summary>
    Deshabilitado,

    /// <summary>La submission no existe.</summary>
    NoEncontrado,

    /// <summary>La submission no está en <c>pendiente</c> (otra réplica la resolvió, o ya es final).</summary>
    EstadoInvalido,

    /// <summary>Fallo reintentable (timeout, red, 5xx). La submission sigue reclamable.</summary>
    ErrorTransitorio,

    /// <summary>
    /// Fallo que reintentar NO arregla (el propietario no mapea a un tipo Quipux, el adjunto no
    /// existe, la parametrización desapareció). Va directo a dead-letter: consumir los 5 intentos
    /// contra un dato que no va a cambiar solo retrasa la visibilidad del problema.
    /// </summary>
    ErrorDefinitivo,

    /// <summary>Agotó <c>max_attempts</c>. Submission <c>fallido</c> + evento <c>dead_letter</c> + log Critical.</summary>
    Fallido,
}

/// <summary>Motivos estables de fallo de radicación. Códigos, no mensajes: viajan a la bitácora y al panel.</summary>
public static class RegistrarDocumentoQuipuxMotivos
{
    /// <summary>El trámite de la submission ya no existe (o fue eliminado).</summary>
    public const string TramiteNoEncontrado = "tramite_no_encontrado";

    /// <summary>El <c>procedure_type</c> perdió el bloque <c>quipux</c> de <c>external_refs</c> entre el encolado y la radicación.</summary>
    public const string TipoSinParametrizacionQuipux = "tipo_sin_parametrizacion_quipux";

    /// <summary>Ni placa ni VIN disponibles para identificar el vehículo.</summary>
    public const string SinIdentificadorVehiculo = "sin_identificador_vehiculo";

    /// <summary>El trámite no tiene actor propietario (o el actor no trae documento).</summary>
    public const string PropietarioNoResuelto = "propietario_no_resuelto";

    /// <summary>
    /// El tipo de documento del propietario no está en el mapa de Quipux. En 1.0 esto NO frenaba la
    /// radicación: <c>mapTypeDocument</c> devolvía el string "Se desconoce el tipo de documento" y
    /// viajaba dentro de un campo numérico del payload.
    /// </summary>
    public const string TipoDocumentoPropietarioNoMapea = "tipo_documento_propietario_no_mapea";

    /// <summary>No se pudo dejar el PDF en el bucket de Quipux.</summary>
    public const string SubidaS3Fallida = "subida_s3_fallida";

    /// <summary>La llamada de radicación falló o expiró.</summary>
    public const string LlamadaRegistroFallida = "llamada_registro_fallida";

    /// <summary>La consulta previa al reintento falló: no se radica sin saber si Quipux ya lo tiene.</summary>
    public const string ConsultaPreviaFallida = "consulta_previa_fallida";

    /// <summary>Quipux respondió un código distinto de 81.</summary>
    public const string RespuestaNoExitosa = "respuesta_no_exitosa";

    /// <summary>Quipux aceptó la radicación pero el trámite no pudo transicionar a <c>entregado</c>.</summary>
    public const string TransicionFallida = "transicion_fallida";
}

/// <summary>Resultado tipado de la radicación. Sin excepciones para el flujo normal.</summary>
public sealed class RegistrarDocumentoQuipuxResult
{
    public RegistrarDocumentoQuipuxStatus Status { get; init; }

    public Guid? SubmissionId { get; init; }

    /// <summary>Código devuelto por Quipux (81 = éxito); <c>0</c> cuando el POST ni siquiera se resolvió.</summary>
    public int? QxCodigo { get; init; }

    /// <summary>Código del motivo (ver <see cref="RegistrarDocumentoQuipuxMotivos"/>); null en el camino feliz.</summary>
    public string? Motivo { get; init; }

    /// <summary>¿El trámite quedó en <c>entregado</c>?</summary>
    public bool TramiteTransicionado { get; init; }

    /// <summary>¿Quipux tiene el documento radicado tras este ciclo?</summary>
    public bool EstaRadicado =>
        Status is RegistrarDocumentoQuipuxStatus.Registrado
            or RegistrarDocumentoQuipuxStatus.YaRegistradoEnQuipux;

    public static RegistrarDocumentoQuipuxResult Registrado(
        Guid submissionId, int codigo, bool transicionado) =>
        new()
        {
            Status = RegistrarDocumentoQuipuxStatus.Registrado,
            SubmissionId = submissionId,
            QxCodigo = codigo,
            TramiteTransicionado = transicionado,
        };

    public static RegistrarDocumentoQuipuxResult YaRegistradoEnQuipux(
        Guid submissionId, int codigo, bool transicionado) =>
        new()
        {
            Status = RegistrarDocumentoQuipuxStatus.YaRegistradoEnQuipux,
            SubmissionId = submissionId,
            QxCodigo = codigo,
            TramiteTransicionado = transicionado,
        };

    public static RegistrarDocumentoQuipuxResult Deshabilitado() =>
        new() { Status = RegistrarDocumentoQuipuxStatus.Deshabilitado };

    public static RegistrarDocumentoQuipuxResult NoEncontrado() =>
        new() { Status = RegistrarDocumentoQuipuxStatus.NoEncontrado };

    public static RegistrarDocumentoQuipuxResult EstadoInvalido(Guid submissionId) =>
        new() { Status = RegistrarDocumentoQuipuxStatus.EstadoInvalido, SubmissionId = submissionId };

    public static RegistrarDocumentoQuipuxResult ErrorTransitorio(
        Guid submissionId, string motivo, int? codigo = null) =>
        new()
        {
            Status = RegistrarDocumentoQuipuxStatus.ErrorTransitorio,
            SubmissionId = submissionId,
            Motivo = motivo,
            QxCodigo = codigo,
        };

    public static RegistrarDocumentoQuipuxResult ErrorDefinitivo(Guid submissionId, string motivo) =>
        new()
        {
            Status = RegistrarDocumentoQuipuxStatus.ErrorDefinitivo,
            SubmissionId = submissionId,
            Motivo = motivo,
        };

    public static RegistrarDocumentoQuipuxResult Fallido(
        Guid submissionId, string motivo, int? codigo = null) =>
        new()
        {
            Status = RegistrarDocumentoQuipuxStatus.Fallido,
            SubmissionId = submissionId,
            Motivo = motivo,
            QxCodigo = codigo,
        };
}

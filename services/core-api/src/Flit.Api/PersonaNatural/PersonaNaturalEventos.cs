namespace Flit.Api.PersonaNatural;

/// <summary>
/// Tipos y datos de los eventos del portal de persona natural que core-api recibe y publica en <c>flit.tramites</c>
/// (Épica #14021, HU #14050 y #14051; contrato en <c>contracts/asyncapi/tramites-events.v1.yaml</c> v1.3.0). Ninguno lleva
/// datos personales: solo identificadores, estados y el motivo en lenguaje claro (que no cita datos de las partes).
/// </summary>
internal static class PersonaNaturalEventos
{
    /// <summary>TRABAJO que deja core-personanatural al radicar el ciudadano (sin datos personales).</summary>
    public const string RadicacionSolicitada = "tramites.persona_natural.radicacion_solicitada";

    /// <summary>core-api resolvió la solicitud: trámite creado o rechazo de negocio (HU #14050 y #14051).</summary>
    public const string SolicitudResuelta = "tramites.persona_natural.solicitud_resuelta";

    /// <summary>Un trámite creado por el portal cambió de estado o abrió una subsanación (HU #14051).</summary>
    public const string EstadoCambiado = "tramites.persona_natural.estado_cambiado";

    /// <summary>Evento de Trámites con el que se detectan los cambios de estado (<c>TramitesEventos.EstadoCambiado</c>).</summary>
    public const string CambioDeEstadoDeTramite = "tramites.procedure.state_changed";

    /// <summary>Productor de las colas de core-api: el exchange <c>flit.tramites</c>.</summary>
    public const string Productor = "tramites";

    /// <summary>Origen con el que nacen los trámites del portal (<c>procedure_instances.origin</c>).</summary>
    public const string Origen = "persona_natural";

    public const string ResultadoCreada = "creada";
    public const string ResultadoRechazada = "rechazada";
}

/// <summary>Un adjunto del aviso: solo identificador, tipo y huella (el resto se pide por el RPC).</summary>
internal sealed record AdjuntoRadicadoDatos(Guid Id, string Tipo, string Sha256);

/// <summary><c>data</c> de <c>tramites.persona_natural.radicacion_solicitada</c> v1: espejo del contrato del portal.</summary>
internal sealed record RadicacionSolicitadaDatos(
    Guid SolicitudId,
    string CodigoPublico,
    string TipoTramiteCodigo,
    int DefinicionVersion,
    string DefinicionHuella,
    Guid OrganismoId,
    IReadOnlyList<AdjuntoRadicadoDatos>? Adjuntos,
    Guid? ComprobantePagoId,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// <c>data</c> de <c>tramites.persona_natural.solicitud_resuelta</c> v1. <see cref="Resultado"/> es <c>creada</c> (con
/// <see cref="ProcedureInstanceId"/> y <see cref="NumeroRadicado"/>) o <c>rechazada</c> (con <see cref="Codigo"/> estable y
/// <see cref="Motivo"/> en lenguaje claro, sin datos técnicos ni personales).
/// </summary>
internal sealed record SolicitudResueltaDatos(
    Guid SolicitudId,
    string Resultado,
    Guid? ProcedureInstanceId,
    string? NumeroRadicado,
    string? Estado,
    string? Codigo,
    string? Motivo,
    DateTimeOffset OcurrioEn);

/// <summary>
/// <c>data</c> de <c>tramites.persona_natural.estado_cambiado</c> v1: el estado actual del trámite del portal. La subsanación
/// no es un estado de FLIT 2 sino un indicador sobre <c>rechazado</c>: <see cref="SubsanacionActiva"/> viaja aparte, con el
/// <see cref="Motivo"/> del rechazo del organismo. <see cref="OcurrioEn"/> ordena eventos repetidos o desordenados en el espejo.
/// </summary>
internal sealed record EstadoCambiadoDatos(
    Guid SolicitudId,
    Guid ProcedureInstanceId,
    string? NumeroRadicado,
    string Estado,
    bool SubsanacionActiva,
    string? Motivo,
    DateTimeOffset OcurrioEn);

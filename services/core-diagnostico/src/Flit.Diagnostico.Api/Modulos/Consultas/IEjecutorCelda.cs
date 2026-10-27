using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Google.Protobuf;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// La lógica de la celda de UNA fuente (HU #13727; desde ADR-0075, «construir solicitud / aplicar respuesta»): ya no llama a
/// core-consultas. La solicitud la arma su <see cref="Bus.IConstructorSolicitud"/> y viaja por el bus; el ejecutor decide cómo
/// queda la celda cuando no hay nada que consultar (<see cref="SinConsultaAsync"/>) y cuando llega la respuesta de
/// core-consultas (<see cref="AplicarAsync"/>). Lee y escribe SOLO con <see cref="ContextoCelda.Db"/>, que ya filtra y valida con
/// la empresa de la celda; quien cierra la celda es el publicador (<see cref="Bus.PublicadorCeldas.CerrarAsync"/>).
/// </summary>
/// <example>
/// <code>
/// var resultado = await ejecutor.AplicarAsync(new ContextoCelda(CeldaEnCurso.De(celda), db), respuesta, ct);
/// </code>
/// </example>
public interface IEjecutorCelda
{
    /// <summary>Fuente que atiende.</summary>
    Fuente Fuente { get; }

    /// <summary>
    /// Construir solicitud, la parte que no viaja: cómo queda la celda si su constructor no armó ninguna consulta (sin vehículo,
    /// sin placa, sin personas, departamento no soportado). Nunca llama a nadie.
    /// </summary>
    Task<ResultadoCelda> SinConsultaAsync(ContextoCelda contexto, CancellationToken ct);

    /// <summary>
    /// Aplicar respuesta: cómo queda la celda con lo que respondió core-consultas a uno de sus trabajos. En las celdas de persona
    /// (<see cref="EjecutorPorPersonas"/>) cada respuesta es de una persona: mientras falten personas devuelve la celda
    /// <see cref="EstadoCelda.EnCurso"/> con lo que ya respondió, y con la última, su cierre.
    /// </summary>
    Task<ResultadoCelda> AplicarAsync(ContextoCelda contexto, RespuestaConsulta respuesta, CancellationToken ct);
}

/// <summary>La celda en curso y la base con la empresa de esa celda.</summary>
public sealed record ContextoCelda(CeldaEnCurso Celda, DiagnosticoDb Db);

/// <summary>
/// Lo que respondió core-consultas a un trabajo de la celda (ADR-0075, <c>consultas.resultado.diagnostico</c>): el
/// <c>*Response</c> proto ya leído (con estado OK) o el código gRPC (con estado ERROR), el instante de la consulta y si salió de la
/// caché de proveedor. <see cref="PersonaId"/> es la persona en las celdas de persona; <see cref="Fuente"/>, la del bus
/// (<c>rues</c> para la persona con NIT de las celdas RUNT de persona).
/// </summary>
/// <param name="PersonaId">Persona de <c>personas_vehiculo</c>; null en las fuentes por vehículo.</param>
/// <param name="Fuente">Fuente del bus (<see cref="Bus.FuentesBus"/>).</param>
/// <param name="Respuesta">El <c>*Response</c> del rpc; null si core-consultas no pudo consultar o no se pudo leer.</param>
/// <param name="Codigo">Código gRPC del error; null con estado OK.</param>
/// <param name="Inicio">Inicio de la consulta (fin menos su duración).</param>
/// <param name="Fin">Cuándo terminó la consulta (el <c>occurredAt</c> del resultado).</param>
/// <param name="DesdeCache">La respuesta salió de la caché de proveedor de core-consultas.</param>
public sealed record RespuestaConsulta(
    Guid? PersonaId, string Fuente, IMessage? Respuesta, StatusCode? Codigo, DateTimeOffset Inicio, DateTimeOffset Fin, bool DesdeCache)
{
    /// <summary>Error de un resultado OK sin respuesta legible (definitivo).</summary>
    public static ErrorConsulta Invalida { get; } =
        new(ErroresFuentePersona.RespuestaInvalida, "core-consultas respondió sin una respuesta válida para la fuente.");

    /// <summary>
    /// La respuesta normalizada con el mapeo de la fuente (<c>Mapeos/*</c>): <paramref name="ok"/> con el <c>*Response</c>,
    /// <paramref name="error"/> con el código gRPC, o <see cref="Invalida"/> si no hay ninguno de los dos.
    /// </summary>
    public RespuestaFuente<T> Mapear<TRespuesta, T>(Func<TRespuesta, RespuestaFuente<T>> ok, Func<StatusCode, RespuestaFuente<T>> error)
        where TRespuesta : class, IMessage
        where T : class
    {
        ArgumentNullException.ThrowIfNull(ok);
        ArgumentNullException.ThrowIfNull(error);
        return Respuesta is TRespuesta r ? ok(r) : Codigo is { } codigo ? error(codigo) : RespuestaFuente<T>.Fallo(Invalida);
    }
}

/// <summary>
/// Lo que se escribe junto con el cierre de una celda, en la MISMA transacción (HU #13730): recibe el contexto de la celda (con
/// su empresa) y la versión del tablero de ese cierre. Lo que modifique con el contexto lo guarda quien cierra; no debe llamar a
/// <c>SaveChanges</c>.
/// </summary>
public delegate Task EfectosDeCierre(DiagnosticoDb db, long versionTablero, CancellationToken ct);

/// <summary>
/// Cómo queda una celda: completada con su dato, en error (con su tipo), N/A con motivo o, en una celda de persona que aún espera
/// a otras personas, en curso con lo que ya respondió; y los efectos que se guardan con el cierre (los datos del vehículo, en la
/// RUNT).
/// </summary>
public sealed record ResultadoCelda(
    EstadoCelda Estado, string? Payload, MotivoNa? MotivoNa, OrigenCelda? Origen, DateTimeOffset? DatoObtenidoEn, string? TipoError = null,
    EfectosDeCierre? Efectos = null)
{
    /// <summary>Con dato de la fuente (objeto JSON normalizado, R-13).</summary>
    public static ResultadoCelda Completado(string payload, DateTimeOffset obtenidoEn, EfectosDeCierre? efectos = null) =>
        new(EstadoCelda.Completado, payload, null, OrigenCelda.Api, obtenidoEn, null, efectos);

    /// <summary>Falló; se puede reprocesar. <paramref name="tipoError"/> es el código de la causa, si se conoce.</summary>
    public static ResultadoCelda Error(string? tipoError = null) => new(EstadoCelda.Error, null, null, null, null, tipoError);

    /// <summary>No aplica, con su motivo.</summary>
    public static ResultadoCelda Na(MotivoNa motivo) => new(EstadoCelda.Na, null, motivo, null, null);

    /// <summary>
    /// Lo que el ejecutor sabe de la consulta para el intento auditable (HU #13737): identificador consultado, servicio que
    /// respondió, transacción del proveedor y mensaje de error. Null si el ejecutor no lo informa (el intento se registra igual).
    /// </summary>
    public DatosIntento? Intento { get; init; }

    /// <summary>
    /// Celda de la misma empresa, del mismo día, cuyo dato se copió sin consultar (HU #13739, D-10): queda en
    /// <c>consultas.reutilizada_de_id</c> y el intento con <c>desde_cache=true</c>. Null si hubo consulta.
    /// </summary>
    public Guid? ReutilizadaDeId { get; init; }

    /// <summary>
    /// Celdas de persona (HU #13792, <see cref="EjecutorPorPersonas"/>): un intento por persona consultada, con su
    /// <c>persona_id</c>, en lugar del intento único de la celda. Null en las fuentes por vehículo.
    /// </summary>
    public IReadOnlyList<IntentoPersona>? IntentosPorPersona { get; init; }

    /// <summary>
    /// Código con que la celda queda en ERROR si NO se reintenta (HU #13792): en una celda de persona, <see cref="TipoError"/>
    /// lleva el transitorio de una persona (para que <c>PoliticaReintentos</c> decida el reintento) y, agotados los reintentos, la
    /// celda queda con este código (<c>PERSONAS_CON_ERROR</c> o la causa común). Null: queda <see cref="TipoError"/>.
    /// </summary>
    public string? TipoErrorSinReintento { get; init; }

    /// <summary>
    /// La respuesta completa de la fuente, ya cifrada, con que queda <c>consultas.respuesta_fuente_cifrada</c> (HU #14087 AC8,
    /// <see cref="RespuestaFuenteCelda"/>): la pone el consumidor de resultados, la copia la reutilización del mismo día y la
    /// conservan (solo las personas OK) el cierre sin consulta y el reconciliador en las celdas de persona. Null: sin respuesta.
    /// </summary>
    public byte[]? RespuestaFuenteCifrada { get; init; }
}

/// <summary>
/// Intento de UNA persona en una celda de persona (HU #13792): la persona consultada, cuándo, el error (null si respondió) y los
/// datos de la consulta (sin identificador: nunca un documento).
/// </summary>
public sealed record IntentoPersona(Guid PersonaId, DateTimeOffset Inicio, DateTimeOffset Fin, string? TipoError, DatosIntento Datos);

/// <summary>Datos de la consulta para <c>intentos_consulta</c> (HU #13737); nunca un documento en claro.</summary>
/// <param name="Identificador">Placa o VIN consultado; null si la consulta fue por persona.</param>
/// <param name="Servicio">Proveedor que respondió; null si no llegó a ninguno.</param>
/// <param name="TransaccionProveedorId">ID de transacción que devolvió el proveedor, si lo trae.</param>
/// <param name="MensajeError">Mensaje técnico del error, sin documentos ni datos del vehículo.</param>
public sealed record DatosIntento(string? Identificador, ServicioConsulta? Servicio, string? TransaccionProveedorId, string? MensajeError);

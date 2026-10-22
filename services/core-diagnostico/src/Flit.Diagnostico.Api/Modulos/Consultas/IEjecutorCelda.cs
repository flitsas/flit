using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Ejecuta la celda de UNA fuente (HU #13727): el procesador genérico de la cola la toma, fija la compañía de la celda y le
/// entrega aquí el contexto. La lógica de cada fuente (RUNT en #13730, las demás después) vive en su ejecutor; el procesador
/// solo toma celdas de las fuentes que tienen uno registrado. El ejecutor lee y escribe SOLO con <see cref="ContextoCelda.Db"/>,
/// que ya filtra y valida con la empresa de la celda, nunca con un <c>DiagnosticoDb</c> inyectado (ese no tiene empresa).
/// </summary>
public interface IEjecutorCelda
{
    /// <summary>Fuente que atiende.</summary>
    Fuente Fuente { get; }

    /// <summary>Consulta la fuente y devuelve cómo queda la celda; quien la cierra es el procesador.</summary>
    Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct);
}

/// <summary>La celda tomada y la base con la empresa de esa celda.</summary>
public sealed record ContextoCelda(CeldaTomada Celda, DiagnosticoDb Db);

/// <summary>
/// Lo que se escribe junto con el cierre de una celda, en la MISMA transacción y solo si el lease sigue siendo de quien la tomó
/// (HU #13730): recibe el contexto de la celda (con su empresa) y la versión del tablero de ese cierre. Lo que modifique con
/// el contexto lo guarda el escritor; no debe llamar a <c>SaveChanges</c>.
/// </summary>
public delegate Task EfectosDeCierre(Persistence.DiagnosticoDb db, long versionTablero, CancellationToken ct);

/// <summary>
/// Cómo queda una celda tras ejecutarla: completada con su dato, en error (con su tipo) o N/A con motivo, y los efectos que se
/// guardan con el cierre (los datos del vehículo, en la RUNT).
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
    /// Lo que el ejecutor sabe de la llamada para el intento auditable (HU #13737): identificador consultado, servicio que
    /// respondió, transacción del proveedor y mensaje de error. Null si el ejecutor no lo informa (el intento se registra igual).
    /// </summary>
    public DatosIntento? Intento { get; init; }

    /// <summary>
    /// Desde cuándo se reintenta (HU #13737): lo fija el procesador con <c>PoliticaReintentos</c> sobre un error transitorio; la
    /// celda vuelve a PENDIENTE con ese <c>disponible_desde</c> en lugar de quedar en ERROR. Null en todo otro caso.
    /// </summary>
    public DateTimeOffset? ReintentarDesde { get; init; }

    /// <summary>
    /// Celda de la misma empresa, del mismo día, cuyo dato se copió sin llamar a la fuente (HU #13739, D-10): la escribe el
    /// escritor en <c>consultas.reutilizada_de_id</c> y el intento queda con <c>desde_cache=true</c>. Null si hubo llamada.
    /// </summary>
    public Guid? ReutilizadaDeId { get; init; }

    /// <summary>
    /// Celdas de persona (HU #13792, <see cref="EjecutorPorPersonas"/>): un intento por persona consultada en esta ejecución, con
    /// su <c>persona_id</c>, en lugar del intento único de la celda. Null en las fuentes por vehículo.
    /// </summary>
    public IReadOnlyList<IntentoPersona>? IntentosPorPersona { get; init; }

    /// <summary>
    /// Código con que la celda queda en ERROR si el procesador NO la reintenta (HU #13792): en una celda de persona,
    /// <see cref="TipoError"/> lleva el transitorio de una persona (para que <c>PoliticaReintentos</c> decida el backoff) y,
    /// agotados los reintentos, la celda queda con este código (<c>PERSONAS_CON_ERROR</c> o la causa común). Null: queda
    /// <see cref="TipoError"/>.
    /// </summary>
    public string? TipoErrorSinReintento { get; init; }
}

/// <summary>
/// Intento de UNA persona en una celda de persona (HU #13792): la persona consultada, cuándo, el error (null si respondió) y los
/// datos de la llamada (sin identificador: nunca un documento).
/// </summary>
public sealed record IntentoPersona(Guid PersonaId, DateTimeOffset Inicio, DateTimeOffset Fin, string? TipoError, DatosIntento Datos);

/// <summary>Datos de la llamada a la fuente para <c>intentos_consulta</c> (HU #13737); nunca un documento en claro.</summary>
/// <param name="Identificador">Placa o VIN consultado; null si la consulta fue por persona.</param>
/// <param name="Servicio">Proveedor que respondió; null si no llegó a ninguno.</param>
/// <param name="TransaccionProveedorId">ID de transacción que devolvió el proveedor, si lo trae.</param>
/// <param name="MensajeError">Mensaje técnico del error, sin documentos ni datos del vehículo.</param>
public sealed record DatosIntento(string? Identificador, ServicioConsulta? Servicio, string? TransaccionProveedorId, string? MensajeError);

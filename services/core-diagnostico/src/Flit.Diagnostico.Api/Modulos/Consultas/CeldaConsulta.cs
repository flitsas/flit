using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Celda del semáforo de un vehículo para una fuente (<c>diagnostico.consultas</c>, HU #13723, plan §5, MOD03/MOD04). También es
/// la unidad de trabajo de la cola durable (ADR a): <see cref="DisponibleDesde"/>, <see cref="LeaseHasta"/> e
/// <see cref="IntentosAuto"/>. Una sola celda por vehículo y fuente. Cuelga del vehículo y del lote con FK compuestas por
/// empresa; si reutiliza el dato de otra celda del mismo día (D-10), esa celda es de la misma empresa.
/// </summary>
public sealed class CeldaConsulta : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Empresa dueña del lote; la misma del vehículo (lo exige la FK compuesta).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Cabecera del lote.</summary>
    public Guid DiagnosticoId { get; init; }

    /// <summary>Vehículo de la celda.</summary>
    public Guid VehiculoId { get; init; }

    public Fuente Fuente { get; init; }

    public EstadoCelda Estado { get; set; } = EstadoCelda.Pendiente;

    /// <summary>Motivo de N/A; presente si y solo si <see cref="Estado"/> es <see cref="EstadoCelda.Na"/>.</summary>
    public MotivoNa? MotivoNa { get; set; }

    /// <summary>
    /// Por qué la celda está en ERROR (HU #13730): el código del <c>TipoErrorFuente</c> de la fuente o
    /// <c>BLOQUEADA_POR_RUNT</c> si no se consultó porque la RUNT del vehículo falló; null fuera de ERROR o sin causa tipada.
    /// </summary>
    public string? TipoError { get; set; }

    /// <summary>De dónde salió el dato; null mientras no hay dato.</summary>
    public OrigenCelda? Origen { get; set; }

    /// <summary>Resultado normalizado (objeto JSON), nunca el crudo del proveedor (R-13).</summary>
    public string? Payload { get; set; }

    /// <summary>Usuario del backoffice que la llenó a mano; nunca va en un DTO de cliente.</summary>
    public Guid? UsuarioManualId { get; set; }

    /// <summary>Fecha y hora del dato que muestra la celda (D-10); la de la celda origen si se reutilizó.</summary>
    public DateTimeOffset? DatoObtenidoEn { get; set; }

    /// <summary>Celda de la misma empresa, consultada el mismo día, cuyo dato se reutilizó sin consumo (D-10).</summary>
    public Guid? ReutilizadaDeId { get; set; }

    /// <summary>Desde cuándo el procesador la puede tomar (backoff de reintentos).</summary>
    public DateTimeOffset DisponibleDesde { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Hasta cuándo la tiene tomada el procesador; null si nadie la tiene.</summary>
    public DateTimeOffset? LeaseHasta { get; set; }

    /// <summary>Intentos automáticos hechos (reintentos de errores transitorios, D-12).</summary>
    public int IntentosAuto { get; set; }

    /// <summary>
    /// Usuario del reproceso pendiente (HU #13746, R-1): la siguiente ejecución de la celda deja su intento con
    /// <c>disparador=REPROCESO</c> y este usuario, y no reutiliza el dato del mismo día. El escritor lo limpia al cerrar la
    /// ejecución; null si la celda no espera un reproceso. Solo con la celda en PENDIENTE o EN_CURSO.
    /// </summary>
    public Guid? ReprocesoUsuarioId { get; set; }

    /// <summary>
    /// Trabajo vigente de la celda en la consulta por RabbitMQ (ADR-0075): descarta trabajos y resultados viejos o duplicados y
    /// reconoce un resultado demorado. Null si la celda no tiene trabajo publicado. Aún no lo lee ni escribe el motor.
    /// </summary>
    public Guid? TrabajoId { get; set; }

    /// <summary>
    /// Republicaciones del reconciliador (ADR-0075, D3): 0 o 1; a la segunda huérfana la celda se cierra en ERROR.
    /// Aún no lo lee ni escribe el motor.
    /// </summary>
    public short Republicaciones { get; set; }

    /// <summary>Último cambio de la celda.</summary>
    public DateTimeOffset ActualizadoEn { get; set; }

    /// <summary>Versión del tablero del lote en el último cambio (polling <c>?desde=</c>).</summary>
    public long VersionTablero { get; set; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>): dos réplicas no se pisan la celda.</summary>
    public uint Version { get; set; }
}

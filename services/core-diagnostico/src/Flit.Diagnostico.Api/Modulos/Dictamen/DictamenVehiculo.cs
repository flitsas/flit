using Flit.Diagnostico.Api.Modulos.Comun;

namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>
/// Dictamen de un vehículo (<c>diagnostico.dictamenes</c>, corte 4, ADR-0077). Clase (a) de ADR-0072: pertenece a la
/// empresa, cuelga del vehículo con la FK compuesta y no se borra. <see cref="Version"/> es la versión de negocio;
/// <see cref="Xmin"/> es el token de concurrencia. La tabla nace vacía: la primera fila la escribe el corte 5.
/// </summary>
public sealed class DictamenVehiculo : IEntidadDeEmpresa
{
    /// <summary>UUID v7 generado por la aplicación.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Empresa dueña del dictamen (la misma del vehículo).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Vehículo dictaminado.</summary>
    public Guid VehiculoId { get; init; }

    /// <summary>Versión de negocio del dictamen de ese vehículo. No es el token de concurrencia.</summary>
    public int Version { get; init; }

    /// <summary>El dictamen que se entrega hoy; false conserva el anterior (no se borra).</summary>
    public bool Vigente { get; set; }

    /// <summary><c>VIABLE</c> o <c>BLOQUEADO</c>. Lo calcula el evaluador; el redactor no lo cambia.</summary>
    public required string Veredicto { get; set; }

    /// <summary>Arreglo JSON de código, texto, efecto y fuente. Sin documentos ni nombres.</summary>
    public required string Alertamientos { get; set; }

    /// <summary>Versión del catálogo con la que se dictaminó. No es FK: la versión no identifica una sola fila.</summary>
    public int CatalogoVersion { get; init; }

    /// <summary><c>PLANTILLA</c> o <c>LLM</c>.</summary>
    public required string RedactadoPor { get; set; }

    /// <summary><c>PENDIENTE</c> o <c>COMPLETADA</c>. Completada si y solo si la redactó el LLM.</summary>
    public required string EstadoRedaccion { get; set; }

    /// <summary>Intentos de la cola de redacción.</summary>
    public int RedaccionIntentos { get; set; }

    /// <summary>Desde cuándo la cola puede tomar la redacción; null si no espera.</summary>
    public DateTimeOffset? RedaccionDisponibleDesde { get; set; }

    /// <summary>Hasta cuándo un trabajador tiene tomada la redacción; null si nadie la tiene.</summary>
    public DateTimeOffset? RedaccionLeaseHasta { get; set; }

    /// <summary>True si un analista reemplazó el veredicto automático. Ningún endpoint lo usa en este corte.</summary>
    public bool AnalisisOverride { get; set; }

    /// <summary>Seudónimo (uuid) de quien hizo el override. Sin documento ni nombre.</summary>
    public Guid? UsuarioOverrideId { get; set; }

    /// <summary>Instante del override; null si no hubo.</summary>
    public DateTimeOffset? OverrideEn { get; set; }

    /// <summary>Instante en que se guardó el dictamen (<c>timestamptz</c>).</summary>
    public DateTimeOffset CreadoEn { get; init; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>). Distinto de <see cref="Version"/>.</summary>
    public uint Xmin { get; set; }
}

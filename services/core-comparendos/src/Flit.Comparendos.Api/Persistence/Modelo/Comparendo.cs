using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Api.Persistence.Modelo;

/// <summary>
/// Tabla maestra (§3.14). Los datos vienen de la fuente principal; la gestión (DP, contraventor, avisos, pagos)
/// es de la gestora. Nunca se borra: se inactiva conservando todo.
/// </summary>
public sealed class Comparendo : Auditada
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Numero { get; set; } = string.Empty;
    public string NumeroNormalizado { get; set; } = string.Empty;
    public TipoIngreso TipoIngreso { get; set; }
    public CanalIngesta? FuentePrincipal { get; set; }
    public string? Placa { get; set; }
    public Guid? IdentificacionMonitoreadaId { get; set; }
    public Guid? PlacaMonitoreadaId { get; set; }
    public string? NitConsultado { get; set; }
    public TipoDocumento? TipoDocumentoInfractor { get; set; }
    public string? NumeroDocumentoInfractor { get; set; }
    public string? NombreInfractor { get; set; }
    public string? CodigoInfraccion { get; set; }
    public string? DescripcionInfraccion { get; set; }
    public string? DireccionInfraccion { get; set; }
    public DateOnly? FechaImposicion { get; set; }
    public TimeOnly? HoraImposicion { get; set; }
    public DateOnly? FechaNotificacion { get; set; }
    public bool? EsFotomulta { get; set; }
    public string? SecretariaTexto { get; set; }
    public Guid? OrganismoTransitoId { get; set; }
    public string? TipoVehiculo { get; set; }
    public string? ServicioVehiculo { get; set; }
    public decimal? ValorCapital { get; set; }
    public decimal? ValorAdicional { get; set; }
    public decimal? ValorTotal { get; set; }
    public string? EstadoFuente { get; set; }
    public bool EnCobroCoactivo { get; set; }
    public string? FotoEvidenciaUrl { get; set; }
    public Guid? ArchivoEvidenciaId { get; set; }
    public DateOnly? FechaLimiteGestion { get; set; }
    public DateOnly? FechaFinDescuento50 { get; set; }
    public DateOnly? FechaFinDescuento25 { get; set; }
    public DateOnly? FechaCaducidad { get; set; }
    public DateOnly? FechaPrescripcion { get; set; }
    public DateTimeOffset? PlazosCalculadosEn { get; set; }
    public bool Activo { get; set; } = true;
    public MotivoInactivacion? MotivoInactivacion { get; set; }
    public string? DetalleInactivacion { get; set; }
    public DateTimeOffset? InactivadoEn { get; set; }
    public Guid? InactivadoPor { get; set; }
    public EstadoGestion EstadoGestion { get; set; } = EstadoGestion.SinGestion;
    public EstadoNotificacionConductor EstadoNotificacionConductor { get; set; } = EstadoNotificacionConductor.SinNotificar;
    public EstadoCambioContraventor EstadoCambioContraventor { get; set; } = EstadoCambioContraventor.SinSolicitar;
    public EstadoConsultaContraventor EstadoConsultaContraventor { get; set; } = EstadoConsultaContraventor.NoAplica;
    public short IntentosConsultaContraventor { get; set; }

    /// <summary>
    /// Si la última consulta a la API de contratos falló: el mensaje y el comienzo de lo que respondió (4000 caracteres),
    /// para diagnosticar. Mismo criterio que las fuentes: la respuesta de una consulta exitosa no se guarda.
    /// </summary>
    public string? ErrorConsultaContraventor { get; set; }
    public bool NotificadoFisicamente { get; set; }
    public bool Pagado { get; set; }
    public bool TieneDp { get; set; }
    public bool Gestionado { get; set; }
    public DateTimeOffset UltimoEventoEn { get; set; }
    public string? LegacyV1Id { get; set; }

    public Contraventor? Contraventor { get; set; }
}

/// <summary>Foto vigente de un comparendo en un canal y clave (§3.15): lo último que trajo y si sigue presente.</summary>
public sealed class ComparendoFuente
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ComparendoId { get; set; }
    public CanalIngesta Canal { get; set; }
    public Guid? IdentificacionMonitoreadaId { get; set; }
    public Guid? PlacaMonitoreadaId { get; set; }
    public string? MunicipioCodigo { get; set; }
    public bool Presente { get; set; } = true;
    public string DatosNormalizados { get; set; } = "{}";
    public string HashDatos { get; set; } = string.Empty;
    public DateTimeOffset PrimeraVezEn { get; set; }
    public DateTimeOffset UltimaVezVistaEn { get; set; }
    public DateTimeOffset? AusenteDesde { get; set; }
    public Guid UltimoCicloId { get; set; }
}

/// <summary>Cualquier cambio de un campo que traiga la fuente (§3.16) [Excel C2].</summary>
public sealed class ComparendoCambio
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ComparendoId { get; set; }
    public CanalIngesta Canal { get; set; }
    public Guid CicloId { get; set; }
    public string Campo { get; set; } = string.Empty;
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public bool AplicadoAMaestro { get; set; }
    public DateTimeOffset RegistradoEn { get; set; }
}

/// <summary>Única línea de tiempo y auditoría del comparendo (§3.17). Solo se agregan filas.</summary>
public sealed class TimelineEvento
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ComparendoId { get; set; }
    public TipoEventoTimeline Tipo { get; set; }
    public DateTimeOffset OcurridoEn { get; set; }
    public ActorTipo ActorTipo { get; set; }
    public Guid? ActorUsuarioId { get; set; }
    public string ActorNombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string? Datos { get; set; }
    public string? RelacionadoTipo { get; set; }
    public Guid? RelacionadoId { get; set; }
}

/// <summary>Una corrida de un trabajo de ingesta (§3.12).</summary>
public sealed class CicloIngesta
{
    public Guid Id { get; set; }
    public CanalIngesta Canal { get; set; }
    public Disparo Disparo { get; set; }
    public Guid? TenantIdManual { get; set; }
    public Guid? SolicitadoPor { get; set; }
    public DateTimeOffset InicioEn { get; set; }
    public DateTimeOffset? FinEn { get; set; }
    public int ClavesTotal { get; set; }
    public int ClavesError { get; set; }
    public ResultadoCiclo Resultado { get; set; } = ResultadoCiclo.EnCurso;
    public string? MensajeError { get; set; }
}

/// <summary>
/// Una llamada real a la fuente; también es la caché del ciclo compartida entre gestoras (MOD01 §10.6). Es el log de la
/// consulta: no guarda la respuesta completa (los comparendos ya quedan con sus datos). Solo si la consulta falló queda
/// el comienzo de lo que respondió la fuente, para diagnosticar.
/// </summary>
public sealed class ConsultaFuente
{
    public Guid Id { get; set; }
    public Guid CicloId { get; set; }
    public CanalIngesta Canal { get; set; }
    public TipoDocumento? TipoDocumento { get; set; }
    public string ClaveConsultada { get; set; } = string.Empty;
    public string? MunicipioCodigo { get; set; }
    public int? HttpStatus { get; set; }
    public ResultadoConsulta Resultado { get; set; }
    public TipoErrorFuente? TipoError { get; set; }
    public string? MensajeError { get; set; }
    public int CantidadItems { get; set; }
    /// <summary>Los primeros <see cref="MaxRespuestaError"/> caracteres de la respuesta, solo cuando la consulta falló.</summary>
    public string? RespuestaError { get; set; }
    public int DuracionMs { get; set; }
    public DateTimeOffset ConsultadoEn { get; set; }

    public const int MaxRespuestaError = 4000;
}

/// <summary>Resultado por gestora y clave monitoreada en un ciclo (§3.12): lo que ve el log de migración.</summary>
public sealed class LogMigracion
{
    public Guid Id { get; set; }
    public Guid CicloId { get; set; }
    public Guid TenantId { get; set; }
    public CanalIngesta Canal { get; set; }
    public Guid? IdentificacionMonitoreadaId { get; set; }
    public Guid? PlacaMonitoreadaId { get; set; }
    public string? Clave { get; set; }
    public string? MunicipioCodigo { get; set; }
    public DateTimeOffset InicioEn { get; set; }
    public DateTimeOffset? FinEn { get; set; }
    public int CantidadObtenidas { get; set; }
    public int CantidadNuevas { get; set; }
    public int CantidadActualizadas { get; set; }
    public int CantidadSinCambios { get; set; }
    public int CantidadDuplicadasOtraFuente { get; set; }
    public int CantidadInactivadas { get; set; }
    public int CantidadReactivadas { get; set; }
    public int CantidadDpFavorableAutomatico { get; set; }
    public ResultadoCiclo Resultado { get; set; }
    public string? MensajeError { get; set; }
}

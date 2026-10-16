using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Api.Persistence.Modelo;

/// <summary>Capacidades y parámetros de una gestora (§3.2). Una fila por empresa, creada con valores por defecto.</summary>
public sealed class ConfiguracionEmpresa : Auditada
{
    public Guid TenantId { get; set; }
    public string? EmpresaNombre { get; set; }

    /// <summary>NIT de la gestora (base sin DV), para el membrete de los documentos ({{empresa.nit}}).</summary>
    public string? EmpresaNit { get; set; }
    public bool HasRightPetitionModule { get; set; }
    public bool AllowsContravenerConsultation { get; set; }
    public bool AllowsContravenerChange { get; set; }
    public bool HasOcrAccess { get; set; }
    public bool HasReportesModule { get; set; }
    public CanalComunicacion NotificationChannel { get; set; } = CanalComunicacion.Email;
    public CanalCorreo CanalCorreo { get; set; } = CanalCorreo.FlitSmtp;
    public string? RemitenteNombre { get; set; }
    public string? CorreoRespuesta { get; set; }

    /// <summary>JSON <c>[{correo, tipos:[TipoComunicacion…]}]</c>; tipos vacío = todos.</summary>
    public string Cc { get; set; } = "[]";
    public string Bcc { get; set; } = "[]";
    public TipoIntegracionConductor TipoIntegracionConductor { get; set; } = TipoIntegracionConductor.Manual;
    public ConjuntoPlantillas ConjuntoPlantillas { get; set; } = ConjuntoPlantillas.Flit;
    public bool RequiereRepresentanteLegal { get; set; } = true;
    public List<string> DestinatariosReportes { get; set; } = [];
    public List<string> CorreosAlertaOperativa { get; set; } = [];
}

/// <summary>NIT (u otro documento) que vigila la gestora (§3.3).</summary>
public sealed class IdentificacionMonitoreada : Auditada
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public TipoDocumento TipoDocumento { get; set; } = TipoDocumento.NIT;
    public string NumeroBase { get; set; } = string.Empty;
    public short? DigitoVerificacion { get; set; }
    public string? NombreTitular { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Placa que vigila la gestora, independiente de sus NITs (§3.4).</summary>
public sealed class PlacaMonitoreada : Auditada
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Placa { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;

    /// <summary>ADMIN u OCR (registrada desde un documento OCR).</summary>
    public string OrigenRegistro { get; set; } = "ADMIN";
}

/// <summary>Representante legal que firma DPs y solicitudes: de la gestora o de un NIT (§3.5) [Excel J7, P6, P10].</summary>
public sealed class RepresentanteLegal : Auditada
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public AlcanceRepresentante Alcance { get; set; }
    public Guid? IdentificacionMonitoreadaId { get; set; }
    public TipoDocumento TipoDocumento { get; set; } = TipoDocumento.CC;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string Nombres { get; set; } = string.Empty;
    public string? Cargo { get; set; }
    public string? Correo { get; set; }
    public string? Telefono { get; set; }
    public string? DireccionNotificacion { get; set; }
    public Guid? FirmaArchivoId { get; set; }
    public Guid? CedulaArchivoId { get; set; }
    public Guid? CertificadoArchivoId { get; set; }
    public Guid? EscrituraArchivoId { get; set; }
    public DateOnly? FechaExpedicionCertificado { get; set; }
    public bool PermisoEnvioDp { get; set; } = true;
    public bool PermisoCambioContraventor { get; set; } = true;
    public bool Activo { get; set; } = true;
}

/// <summary>Organismo de tránsito con los campos propios de GDC: correos de radicación [Excel J6] (§3.6).</summary>
public sealed class OrganismoTransito : Auditada
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? CodigoDivipola { get; set; }
    public string? Departamento { get; set; }
    public string? Municipio { get; set; }
    public bool ActivoPlataforma { get; set; } = true;
    public List<string> CorreosRadicacion { get; set; } = [];
    public bool PermiteCambioContraventor { get; set; } = true;
    public DateTimeOffset SincronizadoEn { get; set; }
}

/// <summary>Texto de secretaría que trae una fuente, traducido al OT del catálogo (§3.7).</summary>
public sealed class OrganismoTransitoAlias : Auditada
{
    public Guid Id { get; set; }
    public string TextoNormalizado { get; set; } = string.Empty;
    public Guid OrganismoTransitoId { get; set; }
    public string Origen { get; set; } = "MANUAL";
}

/// <summary>«Admin de OTs bloqueados»: un OT solo se bloquea de forma explícita (§3.8).</summary>
public sealed class PermisoOtEmpresa : Auditada
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrganismoTransitoId { get; set; }
    public Guid? RepresentanteLegalId { get; set; }
    public bool PermiteConsultaContraventor { get; set; } = true;
    public bool PermiteCambioContraventor { get; set; } = true;
}

/// <summary>Fuente municipal habilitada (global, no por gestora) (§3.9).</summary>
public sealed class MunicipioFuente : Auditada
{
    public Guid Id { get; set; }
    public string CodigoFuente { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public Guid? OrganismoTransitoId { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Tipología de DP del catálogo global (§3.24): es un dato, no código.</summary>
public sealed class TipologiaDp : Auditada
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string DescripcionJuridica { get; set; } = string.Empty;
    public string PrefijoRadicado { get; set; } = string.Empty;
    public List<string> EstadosLegalesPermitidos { get; set; } = [];
    public bool PermiteMasivo { get; set; }
    public short PlazoRespuestaOtDiasHabiles { get; set; } = 15;
    public List<string> AdjuntosRequeridos { get; set; } = [];
    public List<string> AliasV2 { get; set; } = [];
    public bool Activo { get; set; } = true;
    public string? LegacyV1Codigo { get; set; }
}

/// <summary>Tipologías habilitadas para una gestora.</summary>
public sealed class TipologiaEmpresa : Auditada
{
    public Guid TenantId { get; set; }
    public Guid TipologiaId { get; set; }
    public bool Habilitada { get; set; }
}

/// <summary>Plantilla de documento o de correo de un conjunto (§3.25).</summary>
public sealed class Plantilla : Auditada
{
    public Guid Id { get; set; }
    public ConjuntoPlantillas Conjunto { get; set; }
    public TipoPlantilla Tipo { get; set; }
    public Guid? TipologiaId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public Guid? VersionPublicadaId { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Versión de una plantilla; inmutable una vez publicada. El cuerpo es HTML con <c>{{variables}}</c>.</summary>
public sealed class PlantillaVersion : Auditada
{
    public Guid Id { get; set; }
    public Guid PlantillaId { get; set; }
    public int NumeroVersion { get; set; }
    public EstadoPlantillaVersion Estado { get; set; } = EstadoPlantillaVersion.Borrador;
    public string? Asunto { get; set; }
    public string CuerpoHtml { get; set; } = string.Empty;
    public List<string> VariablesUsadas { get; set; } = [];
    public string? NotaCambio { get; set; }
    public DateTimeOffset? PublicadaEn { get; set; }
    public Guid? PublicadaPor { get; set; }
}

/// <summary>Trabajo programado (§3.10): expresión cron en hora de Colombia.</summary>
/// <summary>
/// Configuración de consultas de una empresa (HU #13521). Tener el producto encendido no dispara nada: se consulta solo
/// con la configuración ACTIVA (la activa el SuperAdmin), el producto habilitado en la plataforma y la fuente conectada.
/// </summary>
public sealed class ConfiguracionConsultas : Auditada
{
    public Guid TenantId { get; set; }
    public EstadoConfiguracionConsultas Estado { get; set; } = EstadoConfiguracionConsultas.Borrador;
    public DateTimeOffset? ActivadaEn { get; set; }
    public Guid? ActivadaPor { get; set; }
}

/// <summary>
/// Un canal de la configuración de consultas: encendido, proveedor (información interna de FLIT: la empresa nunca lo
/// ve) y agenda en hora de Colombia.
/// </summary>
public sealed class ConfiguracionConsultaCanal : Auditada
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public CanalIngesta Canal { get; set; }
    public bool Encendido { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public Domain.Ingesta.ModoAgenda Modo { get; set; } = Domain.Ingesta.ModoAgenda.HorasFijas;
    public List<string> Horas { get; set; } = [];
    public int? IntervaloMinutos { get; set; }
    public TimeOnly? VentanaDesde { get; set; }
    public TimeOnly? VentanaHasta { get; set; }
    public bool SoloDiasHabiles { get; set; }
    public DateTimeOffset? UltimaEjecucionEn { get; set; }
    public ResultadoCiclo? UltimoResultado { get; set; }

    public Domain.Ingesta.AgendaConsultas Agenda() => new(Modo,
        Horas.Select(Domain.Ingesta.AgendaConsultas.Hora).OfType<TimeOnly>().ToList(), IntervaloMinutos, VentanaDesde, VentanaHasta, SoloDiasHabiles);
}

public sealed class TrabajoProgramado : Auditada
{
    public Guid Id { get; set; }
    public TipoTrabajo Tipo { get; set; }
    public string ExpresionCron { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public bool SoloDiasHabiles { get; set; }
    public DateTimeOffset? UltimaEjecucionEn { get; set; }
    public ResultadoCiclo? UltimoResultado { get; set; }
}

/// <summary>Hora de envío de cada aviso programado (§3.11).</summary>
public sealed class SchedulerAviso : Auditada
{
    public TipoAviso TipoAviso { get; set; }
    public TimeOnly HoraEnvio { get; set; } = new(7, 0);
    public bool Activo { get; set; } = true;
}

/// <summary>Alertas por fallo de integración: una sola fila global (§3.32).</summary>
public sealed class AlertaConfiguracion : Auditada
{
    public short Id { get; set; } = 1;
    public bool AlertasHabilitadas { get; set; } = true;
    public int UmbralFallosCiclo { get; set; }
    public string PlantillaAsunto { get; set; } = "[GDC ALERTA] Fallo en migración {FUENTE} — {TIMESTAMP}";
    public string RemitenteNombre { get; set; } = "FLIT Comparendos";

    /// <summary>Lista interna de FLIT que recibe en copia oculta los reportes automáticos (S-61).</summary>
    public List<string> CorreosInternosReportes { get; set; } = [];
}

public sealed class AlertaDestinatario : Auditada
{
    public Guid Id { get; set; }
    public string Correo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public TipoAlertaDestinatario TipoAlerta { get; set; } = TipoAlertaDestinatario.Todas;
    public bool Activo { get; set; } = true;
}

/// <summary>Prompt de extracción OCR configurable sin desplegar código (§3.28).</summary>
public sealed class PromptOcrVersion : Auditada
{
    public Guid Id { get; set; }
    public int NumeroVersion { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string Proveedor { get; set; } = "ANTHROPIC";
    public string Modelo { get; set; } = string.Empty;
    public bool Publicada { get; set; }

    /// <summary>Cuándo dejó de ser la publicada por última vez. Una versión que ya estuvo publicada se puede volver a publicar sin evaluarla.</summary>
    public DateTimeOffset? RetiradaEn { get; set; }
}

public enum EstadoEvaluacionPrompt
{
    EnCurso,
    Terminada,
    Fallida,
}

/// <summary>
/// Comparación de una versión del prompt con la publicada sobre los mismos documentos ya revisados por una persona. Sin
/// una evaluación aprobada, la versión no se publica.
/// </summary>
public sealed class EvaluacionPromptOcr
{
    public Guid Id { get; set; }
    public Guid PromptVersionId { get; set; }

    /// <summary>La versión publicada contra la que se comparó. Si se publica otra antes, la evaluación ya no sirve.</summary>
    public Guid? PromptVigenteId { get; set; }

    /// <summary>Fecha de la última edición de la versión al evaluarla: si se edita después, hay que evaluarla de nuevo.</summary>
    public DateTimeOffset VersionDel { get; set; }

    public EstadoEvaluacionPrompt Estado { get; set; } = EstadoEvaluacionPrompt.EnCurso;
    public int Documentos { get; set; }
    public int Campos { get; set; }
    public int AciertosCandidata { get; set; }
    public int AciertosVigente { get; set; }
    public bool Aprobada { get; set; }
    public string? Mensaje { get; set; }

    /// <summary>Campo por campo de cada documento: lo esperado y lo que leyó cada versión.</summary>
    public string? Detalle { get; set; }

    public DateTimeOffset CreadoEn { get; set; }
    public Guid? CreadoPor { get; set; }
    public DateTimeOffset? TerminadaEn { get; set; }
    public DateTimeOffset? ReclamadaHasta { get; set; }
}

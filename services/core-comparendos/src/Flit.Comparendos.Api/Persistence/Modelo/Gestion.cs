using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Api.Persistence.Modelo;

/// <summary>Conductor activo del comparendo; uno solo (§3.18).</summary>
public sealed class Contraventor : Auditada
{
    public Guid ComparendoId { get; set; }
    public Guid TenantId { get; set; }
    public TipoDocumento TipoDocumento { get; set; } = TipoDocumento.CC;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string Nombres { get; set; } = string.Empty;
    public string? Correo { get; set; }
    public string? Telefono { get; set; }
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public string? Sucursal { get; set; }
    public string? TipoContrato { get; set; }
    public OrigenContraventor Origen { get; set; }
    public bool DeclaracionTratamientoDatos { get; set; }
    public Guid? RespuestaApiArchivoId { get; set; }
}

/// <summary>Cada asignación y reemplazo de conductor, con motivo obligatorio al reemplazar (§3.18).</summary>
public sealed class ContraventorHistorial
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ComparendoId { get; set; }
    public string? DatosAnteriores { get; set; }
    public string DatosNuevos { get; set; } = "{}";
    public OrigenContraventor Origen { get; set; }
    public string? Motivo { get; set; }
    public Guid? UsuarioId { get; set; }
    public DateTimeOffset RegistradoEn { get; set; }
}

/// <summary>Documento asociado al comparendo: del conductor, comprobantes y soportes (§3.19).</summary>
public sealed class DocumentoSoporte : Auditada
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ComparendoId { get; set; }
    public TipoDocumentoSoporte Tipo { get; set; }
    public Guid ArchivoId { get; set; }
    public OrigenDocumentoSoporte Origen { get; set; } = OrigenDocumentoSoporte.CargaManual;
    public bool Vigente { get; set; } = true;
}

/// <summary>Aviso enviado al conductor (§3.20). Cada aviso automático sale una sola vez.</summary>
public sealed class AvisoConductor
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ComparendoId { get; set; }
    public TipoAviso TipoAviso { get; set; }
    public Disparo Disparo { get; set; }
    public VentanaDescuento VentanaAlEnvio { get; set; }
    public short PorcentajeDescuento { get; set; }
    public DateOnly? FechaLimiteVentana { get; set; }
    public string CorreoDestino { get; set; } = string.Empty;
    public Guid ComunicacionId { get; set; }
    public Guid? PortalEnlaceId { get; set; }
    public Guid? EnviadoPor { get; set; }
    public DateTimeOffset RegistradoEn { get; set; }
}

/// <summary>Solicitud de cambio de contraventor ante el OT, con su desenlace (§3.21).</summary>
public sealed class CambioContraventorSolicitud
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ComparendoId { get; set; }
    public short NumeroIntento { get; set; }
    public Guid OrganismoTransitoId { get; set; }
    public Guid? RepresentanteLegalId { get; set; }
    public string ContraventorSnapshot { get; set; } = "{}";
    public Guid FormatoArchivoId { get; set; }
    public Guid? ComunicacionId { get; set; }
    public Guid? Aviso4ComunicacionId { get; set; }
    public EstadoSolicitudCambio Estado { get; set; } = EstadoSolicitudCambio.Solicitado;
    public string? CausalRechazo { get; set; }
    public DateOnly? FechaRespuestaOt { get; set; }
    public Guid? SoporteRespuestaArchivoId { get; set; }
    public Guid SolicitadoPor { get; set; }
    public Guid? RespondidoPor { get; set; }
    public DateTimeOffset SolicitadoEn { get; set; }
    public DateTimeOffset? RespondidoEn { get; set; }
}

/// <summary>Enlace único al portal del conductor (§3.22). El token viaja solo en el enlace; aquí se guarda su hash.</summary>
public sealed class PortalEnlace
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ComparendoId { get; set; }
    public string ContraventorDocumento { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiraEn { get; set; }
    public DateTimeOffset? RevocadoEn { get; set; }
    public string? MotivoRevocacion { get; set; }
    public int Accesos { get; set; }
    public DateTimeOffset? UltimoAccesoEn { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

/// <summary>Centro de mensajes (§3.23): cada correo que sale, con su estado y la apertura por píxel (informativa).</summary>
public sealed class ComunicacionSaliente
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? ComparendoId { get; set; }
    public TipoComunicacion Tipo { get; set; }
    public CanalComunicacion Canal { get; set; } = CanalComunicacion.Email;
    public CanalCorreo? CanalCorreo { get; set; }
    public string? Remitente { get; set; }
    public string? RemitenteNombre { get; set; }

    /// <summary>JSON <c>{para:[…], cc:[…], bcc:[…]}</c>.</summary>
    public string Destinatarios { get; set; } = "{}";
    public string Asunto { get; set; } = string.Empty;
    public Guid CuerpoHtmlArchivoId { get; set; }

    /// <summary>Clave con que se armó (comparendos.aviso_1, comparendos.dp_ot…), para el registro de Notificaciones.</summary>
    public string PlantillaClave { get; set; } = string.Empty;
    public Guid? PlantillaVersionId { get; set; }
    public EstadoComunicacion Estado { get; set; } = EstadoComunicacion.Pendiente;
    public short CantidadReintentos { get; set; }
    public DateTimeOffset? ProximoIntentoEn { get; set; }
    public string? EntregaIdPlataforma { get; set; }
    public string? ResultadoPlataforma { get; set; }
    public string? MotivoFallo { get; set; }
    public bool Desviado { get; set; }
    public DateTimeOffset? AbiertoEn { get; set; }
    public int Aperturas { get; set; }
    public DateTimeOffset? EnviadoEn { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public Guid? CreadoPor { get; set; }
}

public sealed class ComunicacionAdjunto
{
    public Guid Id { get; set; }
    public Guid ComunicacionId { get; set; }
    public Guid ArchivoId { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public sealed class ComunicacionIntento
{
    public Guid Id { get; set; }
    public Guid ComunicacionId { get; set; }
    public short Numero { get; set; }
    public string? ResultadoPlataforma { get; set; }
    public string? Mensaje { get; set; }
    public int DuracionMs { get; set; }
    public DateTimeOffset IntentadoEn { get; set; }
}

/// <summary>Derecho de petición radicado ante el OT (§3.26). Uno por comparendo [Excel J4].</summary>
public sealed class DerechoPeticion : Auditada
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ComparendoId { get; set; }
    public Guid TipologiaId { get; set; }
    public Guid PlantillaVersionId { get; set; }
    public string Radicado { get; set; } = string.Empty;
    public Guid OrganismoTransitoId { get; set; }
    public List<string> CorreosDestino { get; set; } = [];
    public Guid? RepresentanteLegalId { get; set; }
    public string PeticionarioSnapshot { get; set; } = "{}";
    public string EstadoLegalAlRadicar { get; set; } = string.Empty;
    public decimal? ValorAlRadicar { get; set; }
    public Guid DocumentoArchivoId { get; set; }
    public bool FirmadoDigitalmente { get; set; }
    public Guid? ComunicacionId { get; set; }
    public Guid? LoteId { get; set; }
    public EstadoDp Estado { get; set; } = EstadoDp.Enviando;
    public DateOnly? FechaRadicacion { get; set; }
    public DateOnly? FechaLimiteRespuestaOt { get; set; }
    public SentidoRespuestaOt? RespuestaSentido { get; set; }
    public OrigenRespuestaOt? RespuestaOrigen { get; set; }
    public string? RespuestaRadicadoOt { get; set; }
    public DateOnly? RespuestaFecha { get; set; }
    public Guid? RespuestaArchivoId { get; set; }
    public Guid? RespuestaRegistradaPor { get; set; }
    public DateTimeOffset? RespuestaRegistradaEn { get; set; }
    public Guid EnviadoPor { get; set; }
    public string? LegacyV1Id { get; set; }
}

/// <summary>
/// Radicado reservado (Epic #12980: <c>PREFIJO-AAMMDDHHMMSS-mmm</c>, global entre gestoras). La reserva es atómica entre
/// réplicas: dos DP del mismo milisegundo nunca comparten número (en V1 un lote entero compartía uno).
/// </summary>
public sealed class RadicadoReservado
{
    public string Radicado { get; set; } = string.Empty;
    public DateTimeOffset ReservadoEn { get; set; }
}

public sealed class LoteDp
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TipologiaId { get; set; }
    public Guid OrganismoTransitoId { get; set; }
    public EstadoLoteDp Estado { get; set; } = EstadoLoteDp.EnCurso;
    public int Total { get; set; }
    public int Enviados { get; set; }
    public int Omitidos { get; set; }
    public int Fallidos { get; set; }
    public Guid CreadoPor { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? TerminadoEn { get; set; }
}

public sealed class LoteDpItem
{
    public Guid Id { get; set; }
    public Guid LoteId { get; set; }
    public Guid ComparendoId { get; set; }
    public EstadoItemLote Estado { get; set; } = EstadoItemLote.Pendiente;
    public string? Motivo { get; set; }
    public Guid? DerechoPeticionId { get; set; }
}

/// <summary>Pago: marca interna de quién asumió el comparendo; no detiene flujos jurídicos (§3.27).</summary>
public sealed class Pago
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ComparendoId { get; set; }
    public TipoPago TipoPago { get; set; }
    public decimal ValorPagado { get; set; }
    public DateOnly FechaPago { get; set; }
    public Guid? ComprobanteArchivoId { get; set; }
    public OrigenPago Origen { get; set; }
    public Guid? CargaMasivaId { get; set; }
    public Guid RegistradoPor { get; set; }
    public DateTimeOffset RegistradoEn { get; set; }
    public string? LegacyV1Id { get; set; }
}

public sealed class CargaMasivaPagos
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ArchivoId { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public int TotalFilas { get; set; }
    public int FilasValidas { get; set; }
    public int FilasInvalidas { get; set; }
    public EstadoCargaMasiva Estado { get; set; } = EstadoCargaMasiva.Validando;
    public Guid CargadoPor { get; set; }
    public string? CargadoPorNombre { get; set; }
    public DateTimeOffset CargadoEn { get; set; }
    public DateTimeOffset? ProcesadoEn { get; set; }
}

public sealed class CargaMasivaPagosFila
{
    public Guid Id { get; set; }
    public Guid CargaId { get; set; }
    public int NumeroFila { get; set; }
    public string Datos { get; set; } = "{}";
    public bool Valida { get; set; }
    public List<string> Errores { get; set; } = [];
    public Guid? PagoId { get; set; }
}

/// <summary>Paquete de trabajo OCR: hasta 200 documentos (§3.28).</summary>
public sealed class PaqueteOcr
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public EstadoPaqueteOcr Estado { get; set; } = EstadoPaqueteOcr.Pendiente;
    public int CantidadDocumentos { get; set; }
    public int CantidadPendientes { get; set; }
    public int CantidadCompletos { get; set; }
    public int CantidadIncompletos { get; set; }
    public int CantidadFallidos { get; set; }
    public int CantidadDuplicados { get; set; }
    public int CantidadRegistrados { get; set; }
    public int CantidadDescartados { get; set; }
    public Guid PromptVersionId { get; set; }
    public Guid CreadoPor { get; set; }
    public string? CreadoPorNombre { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? ProcesamientoIniciadoEn { get; set; }
    public DateTimeOffset? CerradoEn { get; set; }
}

public sealed class DocumentoOcr
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PaqueteId { get; set; }
    public Guid ArchivoId { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string TipoArchivo { get; set; } = "IMAGE";
    public EstadoDocumentoOcr Estado { get; set; } = EstadoDocumentoOcr.Pendiente;
    public short Intentos { get; set; }
    public string? ProveedorModelo { get; set; }
    public string? RespuestaLlm { get; set; }
    public string? NumeroComparendoExtraido { get; set; }
    public string? NumeroNormalizado { get; set; }
    public List<string> CamposFaltantes { get; set; } = [];
    public Guid? DuplicadoDeComparendoId { get; set; }
    public Guid? DuplicadoDeDocumentoId { get; set; }
    public string? FuenteDuplicado { get; set; }
    public Guid? ComparendoId { get; set; }
    public TipoFalloOcr? TipoFallo { get; set; }
    public string? MensajeError { get; set; }
    public DateTimeOffset? ProximoIntentoEn { get; set; }
    public DateTimeOffset? ProcesadoEn { get; set; }
    public Guid? RevisadoPor { get; set; }
    public DateTimeOffset? RevisadoEn { get; set; }
    public Guid? DescartadoPor { get; set; }
    public string? MotivoDescarte { get; set; }
}

public sealed class DocumentoOcrCampo
{
    public Guid Id { get; set; }
    public Guid DocumentoOcrId { get; set; }
    public string NombreCampo { get; set; } = string.Empty;
    public string? ValorExtraido { get; set; }
    public decimal? Confianza { get; set; }
    public bool EsNull { get; set; }
    public string? ValorCorregido { get; set; }
    public string? ValorFinal { get; set; }
}

public sealed class DocumentoOcrEdicion
{
    public Guid Id { get; set; }
    public Guid DocumentoOcrId { get; set; }
    public string Campo { get; set; } = string.Empty;
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public Guid UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public DateTimeOffset EditadoEn { get; set; }
    public string? Motivo { get; set; }
}

public sealed class DocumentoOcrIntento
{
    public Guid Id { get; set; }
    public Guid DocumentoOcrId { get; set; }
    public short Numero { get; set; }
    public TipoFalloOcr? TipoError { get; set; }
    public string? MensajeError { get; set; }
    public string Resultado { get; set; } = "EXITO";
    public DateTimeOffset IntentadoEn { get; set; }
    public DateTimeOffset? ProximoReintentoEn { get; set; }
}

/// <summary>Exportación a Excel en segundo plano (§3.33).</summary>
public sealed class Exportacion
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SolicitadaPor { get; set; }
    public string Filtros { get; set; } = "{}";
    public int? TotalFilas { get; set; }
    public EstadoExportacion Estado { get; set; } = EstadoExportacion.EnCola;
    public Guid? ArchivoId { get; set; }
    public DateTimeOffset? ExpiraEn { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? TerminadoEn { get; set; }
}

namespace Flit.Comparendos.Domain.Modelo;

// Enumeraciones del modelo (especificación GDC §3.1). Se guardan como texto con su nombre (o smallint en
// TipoIngreso, por compatibilidad con V1), así que renombrar un valor exige migración de datos.

/// <summary>Por dónde entró el comparendo; nunca cambia. Valores de V1 y ADO #12878.</summary>
public enum TipoIngreso : short
{
    SimitNit = 0,
    ManualV1 = 1,
    Ocr = 2,
    OcrComplemento = 3,
    MunicipalNit = 4,
    SimitPlaca = 5,
    MunicipalPlaca = 6,
}

/// <summary>Canal automático: fuente + forma de consulta.</summary>
public enum CanalIngesta
{
    SimitNit,
    SimitPlaca,
    MunicipalNit,
    MunicipalPlaca,
}

/// <summary>Filtro «Fuente» de la bandeja.</summary>
public enum Fuente
{
    Simit,
    Municipal,
    Ocr,
    MigracionV1,
}

/// <summary>Tipos de documento con su código SmartCloud (CC=1 … CV=9; el 5 no existe) [ADO #13029].</summary>
public enum TipoDocumento
{
    CC = 1,
    TI = 2,
    CE = 3,
    NIT = 4,
    PA = 6,
    CD = 7,
    RC = 8,
    CV = 9,
}

public enum MotivoInactivacion
{
    DesaparecidoFuente,
    InactivadoOperador,
}

/// <summary>Gestión jurídica del comparendo (C17: se guarda esto y el estado legal se calcula).</summary>
public enum EstadoGestion
{
    SinGestion,
    DpEnviado,
    RespuestaOt,
}

/// <summary>Último aviso escalonado enviado al conductor.</summary>
public enum EstadoNotificacionConductor
{
    SinNotificar,
    Aviso1,
    Aviso2,
    Aviso3,
}

public enum EstadoCambioContraventor
{
    NoAplica,
    SinSolicitar,
    Solicitado,
    RechazadoOt,
    AceptadoOt,
}

public enum EstadoConsultaContraventor
{
    NoAplica,
    Pendiente,
    Encontrado,
    NoEncontrado,
    Error,
}

public enum OrigenContraventor
{
    Api,
    Manual,
    MigracionV1,
}

public enum EstadoDp
{
    Enviando,
    Enviado,
    EnvioFallido,
    Respondido,
}

public enum SentidoRespuestaOt
{
    Favorable,
    Desfavorable,
}

public enum OrigenRespuestaOt
{
    Operador,
    AutomaticaDesaparicion,
}

/// <summary>CC: Cambio de contraventor · PC: Pago del contraventor · BC: Banco / empresa. El tipo DP desaparece.</summary>
public enum TipoPago
{
    CC,
    PC,
    BC,
}

public enum OrigenPago
{
    Manual,
    MasivoExcel,
    MigracionV1,
}

public enum EstadoPaqueteOcr
{
    Pendiente,
    EnProcesamiento,
    Procesado,
    ProcesadoConErrores,
    Cerrado,
}

public enum EstadoDocumentoOcr
{
    Pendiente,
    EnProcesamiento,
    Completo,
    Incompleto,
    Fallido,
    Duplicado,
    Registrado,
    Descartado,
}

/// <summary>Los cuatro primeros se reintentan (hasta 3 veces); ilegible y archivo inválido no.</summary>
public enum TipoFalloOcr
{
    Timeout,
    Http5xx,
    RateLimit,
    Red,
    Ilegible,
    ArchivoInvalido,
}

public enum TipoComunicacion
{
    Aviso1,
    Aviso2,
    Aviso3,
    Aviso4,
    DpOt,
    CambioContraventorOt,
    AlertaFalloIntegracion,
    AlertaFalloOcr,
    AlertaSinRepresentante,
    ReporteSemanal,
    ReporteMensual,
    ExportacionLista,
}

/// <summary>Solo correo en la primera entrega; WhatsApp y SMS quedan en el backlog [Excel C11].</summary>
public enum CanalComunicacion
{
    Email,
    Whatsapp,
    Sms,
}

/// <summary>Igual que el canal de core-notificaciones: SMTP de FLIT o la API de correo de la empresa (Renting).</summary>
public enum CanalCorreo
{
    FlitSmtp,
    EmpresaApi,
}

public enum EstadoComunicacion
{
    Pendiente,
    Enviado,
    Entregado,
    Abierto,
    Rebotado,
    Fallido,
}

public enum TipoPlantilla
{
    DpDocumento,
    CartaCambioContraventor,
    FormatoCambioContraventor,
    CorreoAviso1,
    CorreoAviso2,
    CorreoAviso3,
    CorreoAviso4,
    CorreoDpOt,
    CorreoCambioContraventorOt,
    CorreoAlertaFallo,
    CorreoAlertaSinRl,
    CorreoReporteSemanal,
    CorreoReporteMensual,

    /// <summary>HU #13884: la autorización de cambio de infractor (PDF) que firma el representante y va en los avisos.</summary>
    AutorizacionCambioInfractor,
}

/// <summary>Dos conjuntos: el de FLIT (cualquier cliente) y el de Renting [Excel J2].</summary>
public enum ConjuntoPlantillas
{
    Flit,
    Renting,
}

public enum EstadoPlantillaVersion
{
    Borrador,
    Publicada,
    Retirada,
}

public enum TipoIntegracionConductor
{
    Manual,
    ApiRenting,
}

public enum AlcanceRepresentante
{
    Empresa,
    Identificacion,
}

public enum TipoDocumentoSoporte
{
    Cedula,
    Licencia,
    CamaraComercio,
    CertificadoRepresentacion,
    EscrituraPoder,
    ComprobantePago,
    RespuestaOt,
    SoporteAceptacionOt,
    Otro,
}

public enum OrigenDocumentoSoporte
{
    CargaManual,
    PortalConductor,
    Sistema,
}

/// <summary>Configuración de consultas de una empresa (HU #13521): solo se consulta con ACTIVA.</summary>
public enum EstadoConfiguracionConsultas
{
    Borrador,
    Activa,
    Pausada,
}

public enum TipoTrabajo
{
    IngestaSimitNit,
    IngestaSimitPlaca,
    IngestaMunicipalNit,
    IngestaMunicipalPlaca,
    AvisosConductor,
    ReintentoContraventor,
    ReporteSemanal,
    ReporteMensual,

    /// <summary>Réplica diaria del catálogo de organismos de tránsito de la plataforma (S-66, HU #13459).</summary>
    SincronizacionOrganismos,
}

public enum Disparo
{
    Programado,
    Manual,
}

public enum ResultadoCiclo
{
    EnCurso,
    Exitoso,
    Parcial,
    Fallido,
}

/// <summary>Solo un 200/201 con lista vacía es «sin comparendos»; lo demás es error y no toca esa fuente [Excel C3].</summary>
public enum ResultadoConsulta
{
    ExitosaConDatos,
    ExitosaVacia,
    Error,
}

public enum TipoErrorFuente
{
    Timeout,
    HttpNoExitoso,
    SoapFault,
    RespuestaInvalida,
    Red,
    Autenticacion,
    EstadoNoOk,
    Excepcion,
}

public enum TipoAlertaDestinatario
{
    Todas,
    SoloCriticas,
    SoloSimit,
    SoloMunicipal,
    SoloOcr,
}

public enum TipoEventoTimeline
{
    Ingreso,
    FuenteAdicional,
    DatosActualizados,
    CambioFuentePrincipal,
    PlazosCalculados,
    InactivadoDesaparicion,
    Reactivado,
    ReaparecioInactivoOperador,
    InactivadoOperador,
    ContraventorConsultado,
    ContraventorAsignado,
    ContraventorReemplazado,
    DocumentoConductorCargado,
    AvisoEnviado,
    ComunicacionEstado,
    PortalAbierto,
    CambioContraventorSolicitado,
    CambioContraventorRechazado,
    CambioContraventorAceptado,
    DpEnviado,
    DpEnvioFallido,
    DpBloqueadoSinRl,
    DpRespuestaRegistrada,
    DpFavorableAutomatico,
    PagoRegistrado,
    CobroCoactivoDetectado,
    NotaManual,
}

public enum ActorTipo
{
    Usuario,
    Sistema,
    Scheduler,
    ApiExterna,
    Conductor,
}

public enum EstadoCargaMasiva
{
    Validando,
    Procesada,
    ProcesadaConErrores,
    Rechazada,
}

public enum EstadoLoteDp
{
    EnCurso,
    Completado,
    CompletadoConErrores,
}

public enum EstadoItemLote
{
    Pendiente,
    Omitido,
    Enviado,
    Fallido,
}

public enum EstadoExportacion
{
    EnCola,
    Generando,
    Lista,
    Fallida,
    Expirada,
}

public enum EstadoSolicitudCambio
{
    Solicitado,
    RechazadoOt,
    AceptadoOt,
    EnvioFallido,
}

public enum TipoAviso
{
    Aviso1,
    Aviso2,
    Aviso3,
    Aviso4,
}

/// <summary>Ventana de descuento vigente el día del envío.</summary>
public enum VentanaDescuento
{
    Descuento50,
    Descuento25,
    SinDescuento,
    SinFechaBase,
}

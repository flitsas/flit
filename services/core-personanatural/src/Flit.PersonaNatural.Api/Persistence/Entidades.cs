namespace Flit.PersonaNatural.Api.Persistence;

/// <summary>
/// Estados de la solicitud del portal (docs/design/PERSONA-NATURAL-arquitectura-y-contratos.md §5.1). El CHECK de la
/// tabla se arma desde esta lista para que código y base no se separen.
/// </summary>
public static class EstadoSolicitud
{
    public const string Borrador = "borrador";
    public const string PendienteValidacion = "pendiente_validacion";
    public const string EnCola = "en_cola";
    public const string Creada = "creada";
    public const string RechazadaCreacion = "rechazada_creacion";
    public const string Cancelada = "cancelada";

    public static readonly IReadOnlyList<string> Todos =
        [Borrador, PendienteValidacion, EnCola, Creada, RechazadaCreacion, Cancelada];
}

/// <summary>Solicitud de un ciudadano para radicar un trámite en FLIT 2. El tipo y su definición son datos, no código.</summary>
public sealed class SolicitudTramite
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string CodigoPublico { get; set; } = string.Empty;
    public string TipoTramiteCodigo { get; set; } = string.Empty;
    public int DefinicionVersion { get; set; }
    public string DefinicionSnapshot { get; set; } = "{}";
    public Guid OrganismoId { get; set; }
    public string OrganismoNombre { get; set; } = string.Empty;
    public string? Placa { get; set; }
    public string? Vin { get; set; }
    public string Estado { get; set; } = EstadoSolicitud.Borrador;

    /// <summary>HMAC de la cédula del solicitante para reanudar; nunca la cédula.</summary>
    public byte[] CedulaSolicitanteHash { get; set; } = [];

    public string FieldValues { get; set; } = "{}";
    public string Datos { get; set; } = "{}";
    public Guid? ProcedureInstanceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? EstadoTramite { get; set; }
    public bool SubsanacionActiva { get; set; }
    public string? SubsanacionMotivo { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset? ExpiraBorradorEn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long RowVersion { get; set; } = 1;
}

/// <summary>Persona de una solicitud (vendedor, comprador, titular…). Dato personal cifrado en la aplicación (AES-GCM).</summary>
public sealed class PersonaSolicitante
{
    public Guid Id { get; set; }
    public Guid SolicitudId { get; set; }

    /// <summary>Rol según la definición del tipo; los roles válidos salen del snapshot, no de un CHECK.</summary>
    public string Parte { get; set; } = string.Empty;

    public short Ordinal { get; set; } = 1;
    public string TipoDocumento { get; set; } = string.Empty;
    public byte[] DocumentoCifrado { get; set; } = [];
    public byte[] DocumentoHash { get; set; } = [];
    public byte[]? NombreCifrado { get; set; }
    public byte[]? CorreoCifrado { get; set; }
    public byte[]? CelularCifrado { get; set; }
    public byte[]? DireccionCifrada { get; set; }
    public Guid? ValidacionId { get; set; }
    public string ValidacionEstado { get; set; } = "sin_solicitar";
    public DateTimeOffset? ValidacionVigenteHasta { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Archivo adjunto de una solicitud; solo se guarda la referencia opaca al almacenamiento.</summary>
public sealed class Adjunto
{
    public Guid Id { get; set; }
    public Guid SolicitudId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Sesión efímera por trámite (el portal no tiene cuentas). Solo se guarda el hash del token.</summary>
public sealed class SesionTramite
{
    public Guid Id { get; set; }
    public Guid SolicitudId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public byte[]? IpHash { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
    public bool Revocada { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Contador diario para los límites por cédula, IP y global; persistente para sobrevivir a los reinicios.</summary>
public sealed class CuotaConsulta
{
    public string Clave { get; set; } = string.Empty;
    public DateOnly Dia { get; set; }
    public int Usadas { get; set; }
}

/// <summary>Constancia del consentimiento de tratamiento de datos (Ley 1581): texto aceptado, momento y huella de quien acepta.</summary>
public sealed class Consentimiento
{
    public Guid Id { get; set; }
    public Guid? SolicitudId { get; set; }
    public byte[] DocumentoHash { get; set; } = [];
    public string TextoVersion { get; set; } = string.Empty;
    public DateTimeOffset AceptadoEn { get; set; }
    public byte[]? IpHash { get; set; }
}

/// <summary>Historial de cambios de estado de una solicitud, tanto del portal como los que llegan de FLIT 2.</summary>
public sealed class HistorialEstado
{
    public Guid Id { get; set; }
    public Guid SolicitudId { get; set; }
    public string? Desde { get; set; }
    public string Hasta { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public string? Motivo { get; set; }
    public Guid? EventId { get; set; }
    public DateTimeOffset OcurrioEn { get; set; }
}

/// <summary>Auditoría de acceso a datos personales. Solo inserción, sin llaves foráneas ni disparadores (como el registro de acceso externo, ADR-0067).</summary>
public sealed class AccesoPiiLog
{
    public long Id { get; set; }
    public DateTimeOffset OcurrioEn { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public Guid? SolicitudId { get; set; }
    public byte[]? IpHash { get; set; }
}

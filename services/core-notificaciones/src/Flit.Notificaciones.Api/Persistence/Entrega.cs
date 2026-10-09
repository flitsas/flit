namespace Flit.Notificaciones.Api.Persistence;

/// <summary>
/// HU #13353 — un intento de envío de correo, salga bien o mal (mismo contenido que
/// <c>admin.notification_delivery_logs</c> de core-api, más quién lo pidió). Solo se agregan filas.
/// </summary>
public sealed class Entrega
{
    public Guid Id { get; set; }

    /// <summary>Null en los correos sin empresa (p. ej. la simulación de mandato).</summary>
    public Guid? TenantId { get; set; }

    public string Plantilla { get; set; } = string.Empty;

    /// <summary><c>flit_smtp</c> o <c>tenant_api</c>.</summary>
    public string Canal { get; set; } = string.Empty;

    public string Destinatario { get; set; } = string.Empty;

    /// <summary><c>enviado</c> o <c>fallido</c>.</summary>
    public string Resultado { get; set; } = string.Empty;

    /// <summary>El <c>EmailSendOutcome</c> del transporte (Sent, ConfigurationIncomplete, …).</summary>
    public string Desenlace { get; set; } = string.Empty;

    public string? MotivoFallo { get; set; }

    public int DuracionMs { get; set; }

    public bool Desviado { get; set; }

    public string? Tema { get; set; }

    public int? TemaVersion { get; set; }

    public string? RemitenteNombre { get; set; }

    /// <summary>Servicio que pidió el envío (producto del token o productor del trabajo).</summary>
    public string Origen { get; set; } = string.Empty;

    /// <summary>Trabajo del bus que lo originó (null si llegó por gRPC).</summary>
    public Guid? TrabajoId { get; set; }

    public DateTimeOffset OcurridoEn { get; set; }
}

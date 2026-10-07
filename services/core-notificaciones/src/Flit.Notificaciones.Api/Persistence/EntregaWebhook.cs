namespace Flit.Notificaciones.Api.Persistence;

/// <summary>HU #13356 — un intento de entrega de un webhook saliente (incluidos los bloqueados por destino interno).</summary>
public sealed class EntregaWebhook
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary><c>ot</c> o <c>ict</c>.</summary>
    public string Origen { get; set; } = string.Empty;

    public string Evento { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    /// <summary><c>entregado</c>, <c>fallido</c> o <c>bloqueado</c>.</summary>
    public string Resultado { get; set; } = string.Empty;

    /// <summary>Código HTTP de la respuesta; null si no hubo respuesta (bloqueado, tiempo agotado, sin conexión).</summary>
    public int? CodigoHttp { get; set; }

    public string? Motivo { get; set; }

    public int DuracionMs { get; set; }

    public string? CorrelacionId { get; set; }

    public Guid TrabajoId { get; set; }

    public DateTimeOffset OcurridoEn { get; set; }
}

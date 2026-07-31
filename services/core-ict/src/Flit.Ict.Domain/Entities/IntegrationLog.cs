namespace Flit.Ict.Domain.Entities;

/// <summary>
/// Log de integración (reemplaza DynamoDB de v1). request/response/headers ya vienen REDACTADOS
/// (doble barrera: redacción en captura + enmascarado al servir).
/// </summary>
public sealed class IntegrationLog
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public string LogType { get; set; } = "transaction";

    public string Direction { get; set; } = "inbound";

    public string Method { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string? Request { get; set; }

    public string? Response { get; set; }

    public string? Headers { get; set; }

    public Guid? CorrelationId { get; set; }

    public int DurationMs { get; set; }

    public string? Usuario { get; set; }

    public DateTime CreatedAt { get; set; }
}

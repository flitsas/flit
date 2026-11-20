namespace Flit.Consultas.Api.Persistence;

/// <summary>Seguimiento TrabajoId → id externo de la consulta asíncrona (D4). Sin PII.</summary>
public sealed class TrabajoImpuestoExterno
{
    public Guid Id { get; set; }

    public Guid TrabajoId { get; set; }

    public Guid TenantId { get; set; }

    public string DepartmentCode { get; set; } = "";

    public string? ExternalId { get; set; }

    public string Status { get; set; } = "reserved";

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? LastPolledAt { get; set; }

    public int PollCount { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

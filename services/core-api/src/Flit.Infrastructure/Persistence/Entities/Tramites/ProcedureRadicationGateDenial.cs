namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Intento rechazado al crear trámite (HU #12348 / #12409).
/// </summary>
public sealed class ProcedureRadicationGateDenial
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid? UserId { get; set; }

    public Guid? TransitOfficeId { get; set; }

    public string DenialReason { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }
}

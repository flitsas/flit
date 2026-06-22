namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureType
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string PublicationStatus { get; set; } = Enums.PublicationStatus.Draft;
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }
    public string ExternalRefs { get; set; } = "{}";
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ICollection<ConformationRule> ConformationRules { get; set; } = [];
    public ICollection<ProcedureStep> Steps { get; set; } = [];
}

namespace Flit.Tramites.Domain.Entities;

public sealed class ConsultationTemplate
{
    public Guid Id { get; set; }
    public Guid ExternalDataSourceId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string EntityScope { get; set; } = string.Empty;
    public string? PersonType { get; set; }
    public string RequiredFieldKeys { get; set; } = "[]";
    public string RequestSchema { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public string ExternalRefs { get; set; } = "{}";
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ExternalDataSource? ExternalDataSource { get; set; }
}

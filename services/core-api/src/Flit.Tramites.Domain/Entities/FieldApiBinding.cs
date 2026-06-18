namespace Flit.Tramites.Domain.Entities;

public sealed class FieldApiBinding
{
    public Guid Id { get; set; }
    public Guid FormFieldId { get; set; }
    public Guid ExternalDataSourceId { get; set; }
    public string TriggerEvent { get; set; } = string.Empty;
    public string RequestMapping { get; set; } = "{}";
    public string ResponseMapping { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public FormField? FormField { get; set; }
    public ExternalDataSource? ExternalDataSource { get; set; }
}

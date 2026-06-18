namespace Flit.Tramites.Domain.Entities;

public sealed class ExternalDataSource
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string AuthType { get; set; } = "none";
    public int TimeoutMs { get; set; } = 5000;
    public bool IsActive { get; set; } = true;
    public string ExternalRefs { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

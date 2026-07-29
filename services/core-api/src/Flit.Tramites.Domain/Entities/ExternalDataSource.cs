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

    /// <summary>
    /// Vigencia (horas) de la caché de reutilización cross-trámite (CF-04, HU #10878, ADR-0030).
    /// NULL = usa el default global <see cref="ExternalQueryCacheRules.DefaultTtlHours"/>.
    /// </summary>
    public int? CacheTtlHours { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

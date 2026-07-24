namespace Flit.Admin.Application.Companies.Deeds.ListDeeds;

/// <summary>
/// Consulta paginada de las escrituras de un tenant. <c>Page</c> y <c>PageSize</c> se saneen en el
/// handler (page ≥ 1; pageSize en [1, 100]).
/// </summary>
public sealed class ListDeedsQuery
{
    public required Guid TenantId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

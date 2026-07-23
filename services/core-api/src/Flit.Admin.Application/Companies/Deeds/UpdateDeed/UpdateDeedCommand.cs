namespace Flit.Admin.Application.Companies.Deeds.UpdateDeed;

/// <summary>
/// Comando de edición de una escritura. <c>Sha256</c> no nulo/vacío = reemplazo del PDF (nuevo
/// artefacto en storage); nulo = conserva el actual.
/// </summary>
public sealed class UpdateDeedCommand
{
    public required Guid TenantId { get; init; }
    public required Guid Id { get; init; }
    public string? Description { get; init; }
    public DateOnly VigenciaDesde { get; init; }
    public DateOnly VigenciaHasta { get; init; }
    public string? Sha256 { get; init; }
    public IReadOnlyList<Guid>? RepresentedCompanyIds { get; init; }
    public Guid? UpdatedBy { get; init; }
}

namespace Flit.Admin.Application.Companies.Deeds.CreateDeed;

/// <summary>
/// Comando de alta de una escritura. El handler valida los metadatos, registra el documento en
/// storage (presigned upload) y persiste la fila (solo path + hash, nunca el PDF) con el puente a
/// compañías. <c>Sha256</c> lo aporta el cliente (hash del PDF a subir).
/// </summary>
public sealed class CreateDeedCommand
{
    public required Guid TenantId { get; init; }
    public string? Description { get; init; }
    public DateOnly VigenciaDesde { get; init; }
    public DateOnly VigenciaHasta { get; init; }
    public string? Sha256 { get; init; }
    public IReadOnlyList<Guid>? RepresentedCompanyIds { get; init; }

    /// <summary>
    /// Representante que asocia la escritura (Feature #10929). El alta se hace desde el detalle del
    /// representante, que lo inyecta; queda persistido para filtrar el detalle y resolver el trámite.
    /// </summary>
    public Guid? RepresentativeId { get; init; }
    public Guid? CreatedBy { get; init; }
}

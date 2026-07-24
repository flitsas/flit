using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.Companies.Deeds;

/// <summary>
/// Vista de una escritura para la gestión admin (HU #10902, ADR-0033). No expone el binario del PDF:
/// solo metadatos + la referencia al artefacto (<c>StoragePath</c>/<c>StorageSha256</c>, id opaco +
/// integridad) y los ids de las compañías a las que aplica. El PDF se ve vía presigned URL de vida
/// corta (endpoint de detalle), nunca por una URL pública.
/// </summary>
public sealed record DeedResponse(
    Guid Id,
    string Description,
    string StoragePath,
    string StorageSha256,
    DateOnly VigenciaDesde,
    DateOnly VigenciaHasta,
    bool IsActive,
    IReadOnlyList<Guid> RepresentedCompanyIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static DeedResponse From(DeedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new DeedResponse(
            item.Id,
            item.Description,
            item.StoragePath,
            item.StorageSha256,
            item.VigenciaDesde,
            item.VigenciaHasta,
            item.IsActive,
            item.RepresentedCompanyIds,
            item.CreatedAt,
            item.UpdatedAt);
    }
}

/// <summary>
/// Detalle de una escritura: la vista base + la presigned URL de visualización inline del PDF
/// (vida corta). <c>ViewUrl</c>/<c>ViewUrlExpiresAt</c> son <c>null</c> si el binario no está
/// disponible en storage.
/// </summary>
public sealed record DeedDetailResponse(
    DeedResponse Deed,
    string? ViewUrl,
    DateTimeOffset? ViewUrlExpiresAt);

/// <summary>
/// Envelope del listado paginado de escrituras: <c>{ data, totalCount, page, pageSize }</c>.
/// </summary>
public sealed record PagedDeedsResponse(
    IReadOnlyList<DeedResponse> Data,
    long TotalCount,
    int Page,
    int PageSize);

namespace Flit.Admin.Domain.Banners;

/// <summary>
/// Banner visible para consumo publico (HU #12240, AC1): id (para construir la URL de imagen),
/// nombre y enlace opcional. No incluye image_storage_path ni image_sha256 (ADR-0057: nunca se
/// expone el path opaco de storage al frontend).
/// </summary>
public sealed record ActiveBannerItem(Guid Id, string Name, string? LinkUrl);

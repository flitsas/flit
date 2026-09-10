namespace Flit.Admin.Domain.Banners;

/// <summary>
/// Referencia de storage de la imagen de un banner (HU #12240, AC2). Solo la usa el handler del
/// endpoint de imagen para abrir el binario y calcular el ETag: nunca sale al frontend
/// (ADR-0057).
/// </summary>
public sealed record BannerImageRef(string StoragePath, string Sha256);

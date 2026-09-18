using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding;

/// <summary>Error de validación de un activo de marca (código estable + campo opcional + detalle libre).</summary>
public sealed record BrandAssetValidationError(string Code, string? Field = null, IReadOnlyDictionary<string, object?>? Details = null);

/// <summary>
/// Punto de extensión para las reglas de formato, longitud, contraste WCAG y dimensiones de la marca
/// (HU #12413 AC1-AC6). HU #12412 registra <see cref="PermissiveBrandAssetValidator"/> — no rechaza
/// nada — para no adelantar reglas que no le corresponden; #12413 sustituye el registro en DI por la
/// implementación real sin tocar los handlers de esta HU.
/// </summary>
public interface IBrandAssetValidator
{
    /// <summary>Nombre, formato de colores y demás reglas del borrador (#12413 AC3-AC5). Vacío = válido.</summary>
    IReadOnlyList<BrandAssetValidationError> ValidateDraft(BrandingDraft draft);

    /// <summary>Formato, peso y dimensiones del logotipo (#12413 AC1-AC2). Vacío = válido.</summary>
    IReadOnlyList<BrandAssetValidationError> ValidateLogo(string contentType, long sizeBytes, int widthPx, int heightPx);
}

using Flit.Tramites.Domain.Tramites.Catalog;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Validación de carga de adjuntos <b>por tipo</b> (HU #10520, RF08/09/10) con respaldo a los
/// límites globales de <see cref="AttachmentRules"/>.
///
/// Diseño aditivo y sin regresión:
/// <list type="bullet">
///   <item>Si el <c>tipo</c> existe en el catálogo, se usan sus <c>mime_types_allowed</c> y
///   <c>max_size_bytes</c>; si alguno viene vacío/0, se usa el respaldo global.</item>
///   <item>Si el <c>tipo</c> NO está en el catálogo, se aplican exactamente los límites globales
///   actuales (mismo comportamiento que <see cref="AttachmentRules.Validate"/>).</item>
///   <item>Aceptación de tipo = whitelist legacy ∪ catálogo activo (nunca rechaza un tipo que
///   hoy se acepta).</item>
/// </list>
/// El catálogo es opcional: cuando es <c>null</c> (p. ej. en tests que construyen el handler sin
/// DI) la validación degrada al comportamiento global hard-coded.
/// </summary>
public sealed class AttachmentValidator(IDocumentTypeCatalog? catalog = null)
{
    public async Task<string?> ValidateAsync(
        string? tipo,
        string? mimetype,
        long sizeBytes,
        CancellationToken ct = default)
    {
        if (sizeBytes <= 0)
        {
            return "missing_file";
        }

        if (string.IsNullOrWhiteSpace(tipo))
        {
            return "invalid_tipo";
        }

        var normalized = tipo.Trim();
        var rule = catalog is null ? null : await catalog.GetRuleAsync(normalized, ct).ConfigureAwait(false);

        // Aceptación aditiva: legacy whitelist ∪ catálogo activo.
        if (!AttachmentRules.ValidTipos.Contains(normalized) && rule is null)
        {
            return "invalid_tipo";
        }

        var allowedMimes = rule is { MimeTypesAllowed.Count: > 0 }
            ? rule.MimeTypesAllowed
            : (IReadOnlyCollection<string>)AttachmentRules.ValidMimetypes;

        if (string.IsNullOrWhiteSpace(mimetype)
            || !allowedMimes.Any(m => string.Equals(m, mimetype, StringComparison.OrdinalIgnoreCase)))
        {
            return "invalid_mime";
        }

        var maxSize = rule is { MaxSizeBytes: > 0 } ? rule.MaxSizeBytes : AttachmentRules.MaxSizeBytes;
        if (sizeBytes > maxSize)
        {
            return "file_too_large";
        }

        return null;
    }
}

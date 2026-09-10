using Flit.Tramites.Domain.Tramites.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del catálogo <see cref="IDocumentTypeCatalog"/> (HU #10520).
///
/// <c>tramites.document_types</c> es un catálogo global (sin RLS). El match es por <c>code</c>
/// exacto (los tipos operativos son minúscula, ej. <c>factura</c>/<c>soat</c>): así se evita
/// colisionar con códigos de otro casing (ej. seed OT <c>SOAT</c>) y, si el tipo no está en el
/// catálogo, se devuelve <c>null</c> ⇒ la validación aplica los límites globales de respaldo.
/// </summary>
internal sealed class DocumentTypeCatalog(FlitDbContext db) : IDocumentTypeCatalog
{
    public async Task<DocumentTypeRule?> GetRuleAsync(string tipo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tipo))
        {
            return null;
        }

        var code = tipo.Trim();
        var row = await db.DocumentTypes
            .AsNoTracking()
            .Where(d => d.IsActive && d.Code == code)
            .Select(d => new { d.Code, d.MimeTypesAllowed, d.MaxSizeBytes, d.UploadInstructions })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return row is null
            ? null
            : new DocumentTypeRule(row.Code, row.MimeTypesAllowed, row.MaxSizeBytes, row.UploadInstructions);
    }

    public async Task<IReadOnlySet<string>> ListSystemGeneratedCodesAsync(CancellationToken ct = default)
    {
        var codes = await db.DocumentTypes
            .AsNoTracking()
            .Where(d => d.IsActive && d.IsSystemGenerated)
            .Select(d => d.Code)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    }
}

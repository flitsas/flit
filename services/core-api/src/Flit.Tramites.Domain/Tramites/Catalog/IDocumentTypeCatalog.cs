namespace Flit.Tramites.Domain.Tramites.Catalog;

/// <summary>
/// Regla de validación de carga resuelta del catálogo <c>tramites.document_types</c> (HU #10520).
/// <see cref="MimeTypesAllowed"/> vacío o <see cref="MaxSizeBytes"/> = 0 significan "usar el
/// respaldo global" (los límites hard-coded actuales), nunca un rechazo.
/// </summary>
public sealed record DocumentTypeRule(
    string Code,
    IReadOnlyList<string> MimeTypesAllowed,
    long MaxSizeBytes,
    // HU #12065/#12066 — instrucción de cargue parametrizada por el admin. null ⇒ el tipo no tiene
    // texto configurado y la tarjeta del paso Requisitos no muestra ninguno.
    string? UploadInstructions = null);

/// <summary>
/// Puerto de solo lectura del catálogo de tipos de documento, usado por la validación de
/// adjuntos para aplicar reglas por tipo (MIME/tamaño) con respaldo a los límites globales.
/// La implementación (EF Core) vive en Infraestructura.
/// </summary>
public interface IDocumentTypeCatalog
{
    /// <summary>
    /// Devuelve la regla del tipo activo cuyo <c>code</c> coincide exactamente con
    /// <paramref name="tipo"/>, o <c>null</c> si no existe en el catálogo (⇒ respaldo global).
    /// </summary>
    Task<DocumentTypeRule?> GetRuleAsync(string tipo, CancellationToken ct = default);

    /// <summary>
    /// Códigos de <c>document_types</c> con <c>is_system_generated</c>. El checklist de carga y los
    /// gates de radicación los omiten; el consolidado sigue incluyéndolos.
    /// </summary>
    Task<IReadOnlySet<string>> ListSystemGeneratedCodesAsync(CancellationToken ct = default);
}

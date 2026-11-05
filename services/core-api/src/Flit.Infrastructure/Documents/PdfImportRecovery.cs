namespace Flit.Infrastructure.Documents;

/// <summary>
/// HU #14312 — resultado de <see cref="PdfModifyNormalizer.EnsureImportable"/>.
/// </summary>
internal enum PdfImportRecovery
{
    /// <summary>El PDF original abre en Import; sin cambios.</summary>
    None = 0,

    /// <summary>Rasterizado con PDFium → QuestPDF; contenido visual conservado.</summary>
    Converted = 1,

    /// <summary>Último recurso: página de aviso Import-ok (HU #14240).</summary>
    Placeholder = 2,
}

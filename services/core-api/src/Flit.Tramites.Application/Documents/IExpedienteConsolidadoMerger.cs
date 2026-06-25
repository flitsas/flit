namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Normaliza adjuntos (PDF o imagen) a páginas PDF y los fusiona en un único expediente consolidado.
/// Implementación en Infrastructure (<see cref="PdfExpedienteConsolidadoMerger"/>).
/// </summary>
public interface IExpedienteConsolidadoMerger
{
    /// <summary>Convierte un adjunto a bytes PDF (pasa-through si ya es PDF).</summary>
    byte[] NormalizeToPdf(byte[] content, string mimetype);

    /// <summary>Fusiona múltiples PDFs en orden en un solo documento.</summary>
    byte[] Merge(IReadOnlyList<byte[]> pdfParts);
}

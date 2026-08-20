namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Perfil de estampado del pie de página (ADR-0049): controla el margen inferior del sello del
/// nombre del documento porque esa geometría es propiedad del <b>tipo de documento</b>, no del tema
/// global. <see cref="Default"/> es el margen histórico (mandato, solicitud, compraventa,
/// certificados); <see cref="Formulario"/> es el margen reducido para las páginas del FUR (hoja 1 y
/// hoja 2, en los tres formatos), que traen contenido oficial más cerca del borde inferior.
/// </summary>
public enum StampProfile
{
    Default,
    Formulario,
}

/// <summary>Una parte del expediente: su PDF y el nombre a mostrar en el pie (HU #10858).</summary>
public sealed record MergePart(byte[] Pdf, string? DocumentName = null, StampProfile Profile = StampProfile.Default);

/// <summary>
/// Datos de la portada del expediente (HU #10857). La capa Application los arma desde la instancia;
/// la Infrastructure los proyecta al generador de portada de marca. Valores nulos/vacíos se pintan
/// como marcador seguro.
/// </summary>
public sealed record ExpedienteCoverInfo(
    string CodigoTramite,
    string? Placa,
    string? TipoTramite,
    string? SecretariaTransito,
    string? CompaniaRadicadora);

/// <summary>
/// Solicitud de composición del expediente consolidado (compositor, ADR-0030):
/// portada (opcional) + partes en orden, con etiqueta por documento para el pie (HU #10858) y el
/// estado del trámite para la marca de agua (HU #10858).
/// </summary>
public sealed record MergeRequest(
    IReadOnlyList<MergePart> Parts,
    ExpedienteCoverInfo? Cover = null,
    string? EstadoTramite = null);

/// <summary>
/// Normaliza adjuntos (PDF o imagen) a páginas PDF y compone el expediente consolidado.
/// Implementación en Infrastructure (<see cref="PdfExpedienteConsolidadoMerger"/>).
/// </summary>
public interface IExpedienteConsolidadoMerger
{
    /// <summary>Convierte un adjunto a bytes PDF (pasa-through si ya es PDF).</summary>
    byte[] NormalizeToPdf(byte[] content, string mimetype);

    /// <summary>Fusiona múltiples PDFs en orden en un solo documento (sin portada ni estampado).</summary>
    byte[] Merge(IReadOnlyList<byte[]> pdfParts);

    /// <summary>
    /// Compone el expediente: antepone la portada (si se provee) y fusiona las partes. El pie por
    /// documento y la marca de agua de estado se aplican según la <see cref="MergeRequest"/> (HU #10858).
    /// </summary>
    byte[] Compose(MergeRequest request);
}

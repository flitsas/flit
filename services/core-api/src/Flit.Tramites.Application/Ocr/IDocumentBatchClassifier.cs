namespace Flit.Tramites.Application.Ocr;

/// <summary>
/// Un documento reconocido dentro de un archivo del lote: qué tipo es y qué páginas ocupa.
/// </summary>
/// <param name="Tipo">Tipo del catálogo OCR (factura | aduana | impronta | soat | rtm).</param>
/// <param name="Paginas">Páginas que ocupa, base 1. Vacío en imágenes de una sola página se normaliza a [1].</param>
/// <param name="Confianza">Certeza del clasificador, 0.0–1.0. La UI la usa para ordenar la revisión.</param>
/// <param name="Motivo">Frase corta del clasificador explicando por qué lo clasificó así (se muestra al operador).</param>
public sealed record ClassifiedDocument(
    string Tipo,
    IReadOnlyList<int> Paginas,
    double Confianza,
    string? Motivo = null);

/// <summary>
/// Resultado de clasificar UN archivo del lote. En éxito trae el mapa de documentos reconocidos y las
/// páginas que no correspondían a ningún tipo esperado; en fallo, <see cref="Status"/> +
/// <see cref="Message"/> con el mismo contrato de degradación que <see cref="DocumentOcrAnalysis"/>
/// (503 proveedor caído, mensaje apto para mostrar al operador).
/// </summary>
/// <param name="Ok">true → clasificación correcta.</param>
/// <param name="TotalPaginas">Total de páginas del archivo según el clasificador (1 para imágenes).</param>
/// <param name="Documentos">Documentos reconocidos. Puede venir vacío: un archivo sin nada aprovechable es un resultado válido, no un error.</param>
/// <param name="PaginasNoReconocidas">Páginas que no corresponden a ningún tipo esperado, base 1.</param>
public sealed record BatchClassification(
    bool Ok,
    int TotalPaginas,
    IReadOnlyList<ClassifiedDocument> Documentos,
    IReadOnlyList<int> PaginasNoReconocidas,
    int Status = 200,
    string? Message = null)
{
    /// <summary>Fallo de clasificación con código HTTP y mensaje legible.</summary>
    public static BatchClassification Failure(int status, string? message) =>
        new(false, 0, [], [], status, message);
}

/// <summary>
/// Clasificador de documentos del cargue masivo. Es el inverso del <see cref="IDocumentOcrAnalyzer"/>:
/// el analizador responde "¿este archivo ES una factura válida?" y el clasificador responde "¿QUÉ hay
/// dentro de este archivo y en qué páginas?". Se ejecuta UNA vez por archivo con el modelo fuerte; los
/// recortes que salen de su mapa los verifica después el analizador por tipo, que es barato.
/// Mismo patrón contract-first que el analizador: mock por defecto, Anthropic con
/// <c>Ocr:Provider = anthropic</c>, sin tocar los handlers.
/// </summary>
public interface IDocumentBatchClassifier
{
    /// <summary>
    /// Clasifica el contenido de un archivo del lote.
    /// </summary>
    /// <param name="tiposEsperados">
    /// Tipos que el trámite espera (varían por modalidad). El clasificador no propone tipos fuera de esta lista.
    /// </param>
    /// <param name="content">Bytes del archivo (tamaño y magic bytes ya validados por el handler).</param>
    /// <param name="mediaType">MIME resuelto por magic bytes: application/pdf | image/jpeg | image/png.</param>
    Task<BatchClassification> ClassifyAsync(
        IReadOnlyCollection<string> tiposEsperados,
        ReadOnlyMemory<byte> content,
        string mediaType,
        CancellationToken ct);
}

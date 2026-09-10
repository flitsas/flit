namespace Flit.Tramites.Application.Ocr;

/// <summary>
/// MOCK: clasificador determinista del cargue masivo para desarrollo/tests sin Anthropic. No hace IO ni
/// lee el binario: reparte una página por cada tipo esperado (en orden) y deja una página final sin
/// reconocer, de modo que el flujo completo — piezas propuestas + bandeja de no reconocidos — se puede
/// recorrer entero sin API key. Es el proveedor por defecto (<c>Ocr:Provider = mock</c>), igual que
/// <see cref="MockDocumentOcrAnalyzer"/>.
/// </summary>
public sealed class MockDocumentBatchClassifier : IDocumentBatchClassifier
{
    private const string MotivoMock = "Clasificación simulada (Ocr:Provider=mock)";

    public Task<BatchClassification> ClassifyAsync(
        IReadOnlyCollection<string> tiposEsperados,
        ReadOnlyMemory<byte> content,
        string mediaType,
        CancellationToken ct)
    {
        var soportados = tiposEsperados.Where(DocumentOcrPrompts.IsSupported).Distinct(StringComparer.Ordinal).ToList();

        // Una imagen no se puede partir: se clasifica entera como el primer tipo esperado.
        if (mediaType != "application/pdf")
        {
            var documentoUnico = soportados.Count == 0
                ? []
                : new List<ClassifiedDocument> { new(soportados[0], [1], 0.9, MotivoMock) };
            return Task.FromResult(new BatchClassification(true, 1, documentoUnico, [], 200, null));
        }

        var documentos = soportados
            .Select((tipo, i) => new ClassifiedDocument(tipo, [i + 1], 0.9, MotivoMock))
            .ToList();

        // La página extra al final simula lo que en un expediente real son mandatos, cédulas y portadas:
        // material que no corresponde a ningún tipo y que el operador debe resolver a mano.
        var totalPaginas = documentos.Count + 1;
        return Task.FromResult(
            new BatchClassification(true, totalPaginas, documentos, [totalPaginas], 200, null));
    }
}

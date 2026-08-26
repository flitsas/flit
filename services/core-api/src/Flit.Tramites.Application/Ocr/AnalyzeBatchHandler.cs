using System.Text.Json.Nodes;

namespace Flit.Tramites.Application.Ocr;

/// <summary>Un archivo del lote tal como llega del cliente (ya materializado en memoria).</summary>
public sealed record BatchInputFile(string Filename, ReadOnlyMemory<byte> Content);

/// <summary>
/// Una pieza propuesta: un documento reconocido dentro de un archivo del lote, ya recortado y verificado
/// con el prompt de su tipo. El cliente la muestra en la pantalla de revisión y, si el operador la
/// confirma, sube <see cref="ContentBase64"/> al expediente por el flujo presign→S3→register de siempre.
/// </summary>
/// <param name="Tipo">Tipo de documento al que se propone asignar la pieza.</param>
/// <param name="SourceFilename">Archivo del lote del que salió (para que el operador se ubique).</param>
/// <param name="Filename">Nombre propuesto del adjunto.</param>
/// <param name="Mimetype">MIME de la pieza.</param>
/// <param name="SizeBytes">Tamaño de la pieza.</param>
/// <param name="Paginas">Páginas del archivo original que ocupa, base 1.</param>
/// <param name="TotalPaginasOrigen">Páginas del archivo original (para el chip "recorte 3/16 págs").</param>
/// <param name="Confianza">Certeza del clasificador, 0.0–1.0.</param>
/// <param name="Motivo">Por qué el clasificador la reconoció así.</param>
/// <param name="Data">
/// JSON del prompt por tipo — el MISMO que devuelve el cargue campo a campo, para que el cliente
/// reutilice su evaluación (validez de tipo, cruce de VIN) y su tarjeta de resumen sin duplicar reglas.
/// null si el análisis no se pudo hacer.
/// </param>
/// <param name="AnalisisError">Por qué no hay <paramref name="Data"/>; null si el análisis fue bien.</param>
/// <param name="ContentBase64">Bytes de la pieza recortada, listos para subir.</param>
public sealed record BatchPiece(
    string Tipo,
    string SourceFilename,
    string Filename,
    string Mimetype,
    long SizeBytes,
    IReadOnlyList<int> Paginas,
    int TotalPaginasOrigen,
    double Confianza,
    string? Motivo,
    JsonObject? Data,
    string? AnalisisError,
    string ContentBase64);

/// <summary>
/// Lo que el clasificador no supo ubicar en ningún tipo. No lleva binario: el cliente todavía tiene el
/// archivo original, así que la salida es ofrecerle cargarlo a mano en un campo (donde el OCR dirigido
/// vuelve a intentar la extracción) o descartarlo.
/// </summary>
public sealed record BatchUnrecognized(
    string SourceFilename,
    IReadOnlyList<int> Paginas,
    int TotalPaginas);

/// <summary>Archivo del lote que no se pudo procesar, con el motivo en lenguaje del operador.</summary>
public sealed record BatchFileError(string Filename, string Motivo);

/// <summary>
/// Respuesta del cargue masivo. Los tres bloques son la pantalla de revisión: lo que se propone subir,
/// lo que sobró, y lo que ni siquiera se pudo abrir. Un lote donde todo falla sigue siendo un 200 con
/// <see cref="Piezas"/> vacío: el error por archivo es información para el operador, no un fallo de la
/// petición.
/// </summary>
public sealed record AnalyzeBatchResponse(
    IReadOnlyList<BatchPiece> Piezas,
    IReadOnlyList<BatchUnrecognized> NoReconocidos,
    IReadOnlyList<BatchFileError> Errores);

/// <summary>
/// Handler del cargue masivo. Por cada archivo del lote: valida, clasifica UNA vez con el modelo fuerte
/// (<see cref="IDocumentBatchClassifier"/>), recorta las páginas de cada documento reconocido
/// (<see cref="IPdfPageExtractor"/>) y verifica cada recorte con el prompt de su tipo
/// (<see cref="IDocumentOcrAnalyzer"/>, el mismo del cargue campo a campo).
///
/// <para>NO persiste nada ni sube nada: igual que <see cref="AnalyzeDocumentHandler"/>, es stateless y
/// ocurre ANTES del flujo S3. Quien decide qué se sube es el operador en la pantalla de revisión.</para>
///
/// <para>Los .zip se expanden aquí en vez de en el navegador: descomprimir es una línea con
/// <c>System.IO.Compression</c> (ya en .NET) y así el frontend no arrastra una dependencia nueva.</para>
/// </summary>
public sealed class AnalyzeBatchHandler(
    IDocumentBatchClassifier classifier,
    IDocumentOcrAnalyzer analyzer,
    IPdfPageExtractor? pdfExtractor = null)
{
    /// <summary>Archivos por lote. Tope de coste y latencia, no una limitación técnica.</summary>
    public const int MaxFiles = 20;

    /// <summary>Peso total del lote (100 MB).</summary>
    public const long MaxTotalBytes = 100L * 1024 * 1024;

    /// <summary>
    /// Peso por archivo (32 MB) — el tope real de la API de visión. Es cinco veces el del cargue campo a
    /// campo a propósito: el caso típico del lote es justo el expediente escaneado que allí no cabía.
    /// </summary>
    public const long MaxFileBytes = 32L * 1024 * 1024;

    /// <summary>Páginas por PDF. Se valida antes de gastar la llamada al clasificador.</summary>
    public const int MaxPdfPages = 100;

    /// <summary>Recortes analizados en paralelo por archivo (los tipos de una matrícula caben de sobra).</summary>
    private const int MaxParallelAnalyses = 5;

    public async Task<(AnalyzeBatchResponse? Result, OcrFailure? Failure)> HandleAsync(
        IReadOnlyList<string> tiposEsperados,
        IReadOnlyList<BatchInputFile> files,
        CancellationToken ct)
    {
        var tipos = tiposEsperados.Where(DocumentOcrPrompts.IsSupported).Distinct(StringComparer.Ordinal).ToList();
        if (tipos.Count == 0)
            return (null, new OcrFailure(400, "Indica al menos un tipo de documento válido."));
        if (files.Count == 0)
            return (null, new OcrFailure(400, "Adjunta al menos un archivo."));

        var errores = new List<BatchFileError>();
        var expandidos = Expand(files, errores);

        if (expandidos.Count > MaxFiles)
            return (null, new OcrFailure(400, $"Máximo {MaxFiles} archivos por carga. Recibidos: {expandidos.Count}."));

        var total = expandidos.Sum(f => (long)f.Content.Length);
        if (total > MaxTotalBytes)
            return (null, new OcrFailure(400, $"La carga supera el máximo de {MaxTotalBytes / (1024 * 1024)} MB en total."));

        var piezas = new List<BatchPiece>();
        var noReconocidos = new List<BatchUnrecognized>();

        // Secuencial por archivo: cada uno gasta una llamada al modelo fuerte y no queremos disparar
        // N de golpe contra el límite de tasa del proveedor. El paralelismo está dentro del archivo,
        // en los recortes, que son llamadas pequeñas.
        foreach (var file in expandidos)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessFileAsync(file, tipos, piezas, noReconocidos, errores, ct);
        }

        // Orden estable para que la pantalla de revisión no baile entre cargas: por archivo y luego por
        // la primera página que ocupa la pieza.
        var ordenadas = piezas
            .OrderBy(p => p.SourceFilename, StringComparer.Ordinal)
            .ThenBy(p => p.Paginas.Count > 0 ? p.Paginas[0] : 0)
            .ToList();

        return (new AnalyzeBatchResponse(ordenadas, noReconocidos, errores), null);
    }

    private async Task ProcessFileAsync(
        BatchInputFile file,
        IReadOnlyList<string> tipos,
        List<BatchPiece> piezas,
        List<BatchUnrecognized> noReconocidos,
        List<BatchFileError> errores,
        CancellationToken ct)
    {
        if (file.Content.Length == 0)
        {
            errores.Add(new BatchFileError(file.Filename, "El archivo está vacío."));
            return;
        }
        if (file.Content.Length > MaxFileBytes)
        {
            errores.Add(new BatchFileError(file.Filename, $"Supera el máximo de {MaxFileBytes / (1024 * 1024)} MB por archivo."));
            return;
        }
        if (!TryResolveMediaType(file.Content.Span, out var mediaType))
        {
            errores.Add(new BatchFileError(file.Filename, "Formato no admitido en la carga masiva. Usa PDF, JPG o PNG."));
            return;
        }

        var esPdf = mediaType == "application/pdf";

        // Contar páginas es un pre-filtro barato, no un requisito: si el lector local no puede abrir el
        // PDF seguimos adelante, porque el modelo de visión sí puede leer PDFs que PdfSharp rechaza. Lo
        // que se pierde es el recorte (se propone el archivo entero) y el tope de páginas; si el archivo
        // está de verdad dañado, el clasificador falla después y eso sí se reporta.
        var totalPaginas = esPdf ? pdfExtractor?.CountPages(file.Content) : 1;
        if (totalPaginas > MaxPdfPages)
        {
            errores.Add(new BatchFileError(file.Filename, $"Tiene {totalPaginas} páginas y el máximo es {MaxPdfPages}. Divídelo y vuelve a intentarlo."));
            return;
        }

        var clasificacion = await classifier.ClassifyAsync(tipos, file.Content, mediaType, ct);
        if (!clasificacion.Ok)
        {
            errores.Add(new BatchFileError(file.Filename, clasificacion.Message ?? "No se pudo analizar el archivo."));
            return;
        }

        // El total del clasificador es una lectura del modelo; el del extractor es un hecho. Gana el hecho.
        var paginasReales = totalPaginas ?? clasificacion.TotalPaginas;

        if (clasificacion.Documentos.Count == 0)
        {
            noReconocidos.Add(new BatchUnrecognized(
                file.Filename, PaginasDe(clasificacion, paginasReales), paginasReales));
            return;
        }

        var nuevas = await AnalyzePiecesAsync(file, mediaType, esPdf, paginasReales, clasificacion, ct);
        piezas.AddRange(nuevas);

        if (clasificacion.PaginasNoReconocidas.Count > 0)
            noReconocidos.Add(new BatchUnrecognized(
                file.Filename, clasificacion.PaginasNoReconocidas, paginasReales));
    }

    /// <summary>
    /// Recorta y verifica cada documento reconocido. Los análisis van en paralelo (son llamadas pequeñas
    /// al modelo barato) con un tope, y se reordenan al final para no depender de quién termine antes.
    /// </summary>
    private async Task<IReadOnlyList<BatchPiece>> AnalyzePiecesAsync(
        BatchInputFile file,
        string mediaType,
        bool esPdf,
        int totalPaginas,
        BatchClassification clasificacion,
        CancellationToken ct)
    {
        var resultados = new List<BatchPiece>(clasificacion.Documentos.Count);
        using var gate = new SemaphoreSlim(MaxParallelAnalyses);

        var tareas = clasificacion.Documentos.Select(async doc =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var (bytes, mime, nombre) = Recortar(file, mediaType, esPdf, totalPaginas, doc);
                var analisis = await analyzer.AnalyzeAsync(doc.Tipo, bytes, mime, ct);

                return new BatchPiece(
                    doc.Tipo,
                    file.Filename,
                    nombre,
                    mime,
                    bytes.Length,
                    doc.Paginas,
                    totalPaginas,
                    doc.Confianza,
                    doc.Motivo,
                    analisis.Ok ? analisis.Data : null,
                    analisis.Ok ? null : analisis.Message ?? "No se pudo leer el contenido de este documento.",
                    Convert.ToBase64String(bytes.Span));
            }
            finally
            {
                gate.Release();
            }
        });

        resultados.AddRange(await Task.WhenAll(tareas));
        return resultados;
    }

    /// <summary>
    /// Devuelve los bytes que representan al documento: el recorte de sus páginas cuando es un PDF
    /// multi-documento, o el archivo entero cuando es una imagen, un PDF de una página o el documento
    /// abarca todo el PDF. Si el recorte falla se sube el original — degradar a "de más" es preferible a
    /// perder el documento.
    /// </summary>
    private (ReadOnlyMemory<byte> Bytes, string Mimetype, string Filename) Recortar(
        BatchInputFile file, string mediaType, bool esPdf, int totalPaginas, ClassifiedDocument doc)
    {
        var abarcaTodo = doc.Paginas.Count >= totalPaginas;
        if (!esPdf || abarcaTodo || pdfExtractor is null || doc.Paginas.Count == 0)
            return (file.Content, mediaType, file.Filename);

        var recorte = pdfExtractor.ExtractPages(file.Content, doc.Paginas);
        return recorte is null
            ? (file.Content, mediaType, file.Filename)
            : (recorte, "application/pdf", NombreRecorte(file.Filename, doc.Tipo));
    }

    /// <summary>Nombre del recorte: <c>soat_expediente.pdf</c>, para que el operador reconozca su origen.</summary>
    private static string NombreRecorte(string original, string tipo)
    {
        var baseName = Path.GetFileNameWithoutExtension(original);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "documento";
        return $"{tipo}_{baseName}.pdf";
    }

    /// <summary>
    /// Páginas a reportar como no reconocidas cuando el archivo entero no dio nada: las que el
    /// clasificador señaló, o todas si no señaló ninguna.
    /// </summary>
    private static IReadOnlyList<int> PaginasDe(BatchClassification clasificacion, int totalPaginas) =>
        clasificacion.PaginasNoReconocidas.Count > 0
            ? clasificacion.PaginasNoReconocidas
            : Enumerable.Range(1, Math.Max(totalPaginas, 1)).ToList();

    /// <summary>
    /// Expande los .zip del lote en sus entradas. Plano: las subcarpetas del zip se aplanan, y se
    /// ignoran directorios y los metadatos de macOS (<c>__MACOSX</c>), que si no aparecerían como
    /// archivos ilegibles en la pantalla de errores.
    /// </summary>
    private static List<BatchInputFile> Expand(IReadOnlyList<BatchInputFile> files, List<BatchFileError> errores)
    {
        var salida = new List<BatchInputFile>(files.Count);
        foreach (var file in files)
        {
            if (!EsZip(file.Content.Span))
            {
                salida.Add(file);
                continue;
            }
            ExpandZip(file, salida, errores);
        }
        return salida;
    }

    private static void ExpandZip(BatchInputFile file, List<BatchInputFile> salida, List<BatchFileError> errores)
    {
        try
        {
            using var stream = new MemoryStream(file.Content.ToArray(), writable: false);
            using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

            long descomprimido = 0;
            var encontrados = 0;

            foreach (var entry in zip.Entries)
            {
                // Directorios (nombre vacío) y basura de macOS.
                if (string.IsNullOrEmpty(entry.Name) || entry.FullName.StartsWith("__MACOSX", StringComparison.Ordinal))
                    continue;
                if (entry.Name.StartsWith('.'))
                    continue;

                // Cortafuegos de zip bomb: se corta por número de entradas y por peso descomprimido,
                // sin confiar en la longitud declarada en la cabecera.
                if (++encontrados > MaxFiles)
                {
                    errores.Add(new BatchFileError(file.Filename, $"El comprimido trae más de {MaxFiles} archivos; se omitieron los restantes."));
                    break;
                }

                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                CopiarLimitado(entryStream, buffer, MaxFileBytes + 1);

                descomprimido += buffer.Length;
                if (buffer.Length > MaxFileBytes || descomprimido > MaxTotalBytes)
                {
                    errores.Add(new BatchFileError(entry.Name, "El archivo del comprimido supera el tamaño permitido."));
                    continue;
                }

                salida.Add(new BatchInputFile(entry.Name, buffer.ToArray()));
            }

            if (encontrados == 0)
                errores.Add(new BatchFileError(file.Filename, "El comprimido no trae archivos utilizables."));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            errores.Add(new BatchFileError(file.Filename, "No se pudo abrir el comprimido: puede estar dañado o tener contraseña."));
        }
    }

    private static void CopiarLimitado(Stream source, Stream destination, long maxBytes)
    {
        var buffer = new byte[81920];
        long escrito = 0;
        int leido;
        while ((leido = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            escrito += leido;
            if (escrito > maxBytes)
            {
                destination.Write(buffer, 0, (int)(leido - (escrito - maxBytes)));
                return;
            }
            destination.Write(buffer, 0, leido);
        }
    }

    private static bool EsZip(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B &&
        (bytes[2] == 0x03 || bytes[2] == 0x05 || bytes[2] == 0x07);

    /// <summary>
    /// Resuelve el MIME por magic bytes, igual que el cargue campo a campo: PDF, JPG y PNG. WEBP se
    /// admite como adjunto pero el modelo de visión no lo lee, así que aquí se rechaza con mensaje claro
    /// en vez de fallar más adelante.
    /// </summary>
    private static bool TryResolveMediaType(ReadOnlySpan<byte> bytes, out string mediaType)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
        {
            mediaType = "application/pdf";
            return true;
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            mediaType = "image/jpeg";
            return true;
        }
        if (bytes.Length >= 2 && bytes[0] == 0x89 && bytes[1] == 0x50)
        {
            mediaType = "image/png";
            return true;
        }
        mediaType = string.Empty;
        return false;
    }
}

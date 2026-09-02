using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.Ocr;

/// <summary>
/// HU #12036 — endereza un PDF antes de mandarlo al modelo de visión.
///
/// <para><b>Por qué existe.</b> Un escaneo denso y girado no hace que el modelo lea mal: hace que
/// <b>invente</b>. Medido sobre una ficha de homologación, tres archivos byte a byte idénticos
/// devolvieron CHEVROLET CAVALIER, HYUNDAI ELANTRA y HYUNDAI ACCENT cuando el vehículo era un FOTON —
/// los tres con <c>es_valido: true</c> y sin ninguna señal de baja confianza. En un experimento
/// controlado sobre el mismo archivo, la única variante que se leyó bien fue la enderezada; ni siquiera
/// subir la resolución ayudaba. La causa es la orientación, y solo la orientación.</para>
///
/// <para><b>Por qué no rasteriza.</b> La API respeta el campo <c>/Rotate</c> que el propio PDF declara,
/// así que enderezar es reescribir ese número. Eso lo hace PdfSharpCore, que ya está en el árbol de
/// dependencias, y evita rasterizar — que en esta imagen (Alpine/musl) habría obligado a cambiar la
/// imagen base, porque no existe build de PDFium para musl.</para>
///
/// <para><b>Por qué prueba en vez de deducir.</b> La sonda solo dice «derecha / no derecha»; preguntarle
/// el sentido del giro acertó 3 de 4 veces. Así que se prueban los giros de uno en uno y se vuelve a
/// preguntar. Son como mucho cuatro llamadas baratas —la sonda ve UNA página, ~3.300 tokens— frente a
/// una lectura equivocada que nadie detectaría.</para>
///
/// <para><b>Degrada a la identidad.</b> Si algo falla —PDF ilegible, proveedor caído, ninguna vuelta
/// funciona— devuelve el original sin tocar. Nunca lanza y nunca bloquea la carga.</para>
/// </summary>
public sealed class PdfOrientationNormalizer(
    IPdfPageExtractor pages,
    IDocumentOrientationProbe probe,
    ILogger<PdfOrientationNormalizer> logger)
{
    /// <summary>Cuartos de vuelta que se prueban antes de rendirse (90°, 180°, 270°).</summary>
    private const int MaxQuarterTurns = 3;

    /// <summary>
    /// Devuelve el PDF enderezado, o el original si ya lo estaba o si no se pudo enderezar.
    /// <paramref name="pdf"/> debe ser un PDF; con imágenes no aplica y quien llama no debe invocarlo.
    /// </summary>
    public async Task<ReadOnlyMemory<byte>> NormalizeAsync(ReadOnlyMemory<byte> pdf, CancellationToken ct)
    {
        // Se sondea SOLO la primera página: basta para saber cómo salió el escaneo y mantiene la
        // llamada barata aunque el expediente tenga 25 páginas.
        var probePage = pages.ExtractPages(pdf, [1]);
        if (probePage is null)
            return pdf;

        var orientation = await probe.ProbeAsync(probePage, ct);
        if (orientation != PageOrientation.Rotated)
            return pdf;   // derecha, o no se sabe: en ambos casos se deja como está

        for (var turns = 1; turns <= MaxQuarterTurns; turns++)
        {
            if (pages.Rotate(probePage, 1) is not { } giradaProbe)
                break;

            probePage = giradaProbe;
            if (await probe.ProbeAsync(probePage, ct) != PageOrientation.Upright)
                continue;

            // La página de sonda ya se ve derecha: se aplica el MISMO giro al documento entero. Los
            // escáneres giran el lote completo, así que un giro uniforme es lo que corresponde.
            if (pages.Rotate(pdf, turns) is { } enderezado)
            {
                OrientationLog.Straightened(logger, turns * 90);
                return enderezado;
            }

            break;
        }

        OrientationLog.CouldNotStraighten(logger);
        return pdf;
    }
}

/// <summary>Logging source-generated (CA1848). No loguea el contenido del documento.</summary>
internal static partial class OrientationLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "OCR: el documento venía girado y se enderezó {Degrees}° antes de analizarlo")]
    public static partial void Straightened(ILogger logger, int degrees);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OCR: el documento parece girado y no se pudo enderezar; se analiza tal cual")]
    public static partial void CouldNotStraighten(ILogger logger);
}

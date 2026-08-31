using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>Superpone valores sobre plantillas PDF blank con PdfSharpCore.</summary>
public static partial class FurOverlayRenderer
{
    /// <summary>Ancho máximo de la imagen de firma del baúl dentro del campo (el resto es metadatos).</summary>
    private const double SignatureImageMaxWidth = 145;

    /// <summary>
    /// HU #11016 — fracción del ALTO del campo que puede ocupar la firma. El resto es aire: una firma
    /// que llena el campo de borde a borde termina tocando (o pisando) las líneas vecinas del FUR.
    /// </summary>
    private const double SignatureImageMaxHeightRatio = 0.88;

    /// <summary>Separación entre imagen de firma y bloque de metadatos.</summary>
    private const double SignatureSidecarGap = 8;

    /// <summary>Tamaño de fuente del bloque de metadatos junto a la firma.</summary>
    private const double SignatureSidecarFontSize = 3;
    public static byte[] RenderPage1(
        byte[] templatePdf,
        FurFieldManifest manifest,
        IReadOnlyDictionary<string, FurFieldValue> values,
        ILogger? logger = null,
        string? referenceNumber = null)
    {
        ArgumentNullException.ThrowIfNull(templatePdf);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(values);

        using var input = new MemoryStream(templatePdf);
        using var imported = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();
        var page = output.AddPage(imported.Pages[0]);

        using var gfx = XGraphics.FromPdfPage(page);
        var pageFields = manifest.Fields.Where(f => f.Page == 1);
        foreach (var field in pageFields)
        {
            if (!values.TryGetValue(field.Id, out var value))
                continue;
            DrawField(gfx, field, value, logger ?? NullLogger.Instance, referenceNumber);
        }

        using var ms = new MemoryStream();
        output.Save(ms, false);
        return ms.ToArray();
    }

    public static byte[] MergePages(byte[] page1, byte[] page2)
    {
        using var outDoc = new PdfDocument();

        using (var s1 = new MemoryStream(page1))
        using (var d1 = PdfReader.Open(s1, PdfDocumentOpenMode.Import))
            outDoc.AddPage(d1.Pages[0]);

        using (var s2 = new MemoryStream(page2))
        using (var d2 = PdfReader.Open(s2, PdfDocumentOpenMode.Import))
            outDoc.AddPage(d2.Pages[0]);

        using var ms = new MemoryStream();
        outDoc.Save(ms, false);
        return ms.ToArray();
    }

    private static void DrawField(
        XGraphics gfx,
        FurFieldDefinition field,
        FurFieldValue value,
        ILogger logger,
        string? referenceNumber)
    {
        if (value.ImageBytes is { Length: > 0 })
        {
            DrawSignatureImage(gfx, field, value.ImageBytes, value.ImageSidecarText);
            return;
        }

        switch (field.Type)
        {
            case FurFieldType.Checkbox:
                if (string.Equals(value.Text, "X", StringComparison.OrdinalIgnoreCase))
                    DrawCheckbox(gfx, field);
                break;
            case FurFieldType.Image when value.ImageBytes is { Length: > 0 }:
                DrawImage(gfx, field, value.ImageBytes);
                break;
            case FurFieldType.Multiline:
            case FurFieldType.Text:
                if (!string.IsNullOrWhiteSpace(value.Text))
                    DrawText(gfx, field, value.Text!, value.FontSizeDelta, logger, referenceNumber);
                break;
        }
    }

    private static void DrawCheckbox(XGraphics gfx, FurFieldDefinition field)
    {
        var font = CreateFont(field.FontSize > 0 ? field.FontSize + 2 : 10, true);
        var yBaseline = field.Y + field.Size * 0.85;
        gfx.DrawString("X", font, XBrushes.Black, new XPoint(field.X, yBaseline));
    }

    private static void DrawText(
        XGraphics gfx,
        FurFieldDefinition field,
        string text,
        double fontSizeDelta,
        ILogger logger,
        string? referenceNumber)
    {
        // HU #11031 — cuerpo efectivo = el del manifiesto ± el ajuste que traiga el valor, con un
        // mínimo legible. Lo usa el sello de identidad, que va 2pt por debajo del resto del campo.
        var fontSize = Math.Max(3, field.FontSize + fontSizeDelta);
        string[] lines;

        if (field.Type == FurFieldType.Multiline)
        {
            if (field.AutoFit)
            {
                // HU #11256 — opt-in por manifiesto (CF12): solo los campos que declaran
                // `autoFit: true` (hoy, `observations` y `linked_company_name`) pasan por el
                // auto-encaje. Los sellos de firma, también `multiline`, NUNCA entran aquí: siguen
                // partiendo exclusivamente por `\n` explícitos, como hoy, sin medir ni un carácter.
                var fit = FurTextFitter.FitMultiline(
                    text,
                    field.W,
                    field.H,
                    fontSize,
                    (value, size) => gfx.MeasureString(value, CreateFont(size, field.Bold)).Width,
                    elidedChars => LogTextTruncated(logger, referenceNumber ?? "(sin id)", field.Id, elidedChars),
                    field.MinFontSize);
                lines = [.. fit.Lines];
                fontSize = fit.FontSize;
            }
            else
            {
                lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }
        else
        {
            // HU #11048 — un valor más ancho que el campo (razón social larga) se salía del recuadro y
            // pisaba los campos vecinos. Se encaja en la caja declarada del manifiesto: primero
            // reduciendo el cuerpo, después partiendo en líneas si el alto lo admite, y solo como último
            // recurso truncando. Si el texto ya cabía, sale EXACTAMENTE como antes (misma calibración).
            var fit = FurTextFitter.Fit(
                text,
                field.W,
                field.H,
                fontSize,
                (value, size) => gfx.MeasureString(value, CreateFont(size, field.Bold)).Width,
                field.MinFontSize);
            lines = [.. fit.Lines];
            fontSize = fit.FontSize;

            // HU #11643 (AC4) — el aviso de truncado también para los campos `text`. El comentario de
            // LogTextTruncated ya afirmaba que `linked_company_name` (casilla 19) lo disparaba, pero
            // era falso: el callback solo se engancha en la rama multilínea con auto-encaje, y ese
            // campo es de tipo `text`. Una razón social recortada se imprimía a medias sin dejar el
            // menor rastro, que es la peor forma de perder un dato: nadie puede enterarse después.
            // `FurTextFitter.Fit` no admite callback, así que el truncado se detecta por la elipsis
            // que el propio fitter inserta —misma constante en ambas rutas— sin cambiarle la firma.
            var truncado = lines.Any(l => l.Contains(FurTextFitter.EllipsisChar, StringComparison.Ordinal));
            if (truncado && !text.Contains(FurTextFitter.EllipsisChar, StringComparison.Ordinal))
            {
                var impresos = lines.Sum(l => l.Length) - 1; // -1 por la elipsis añadida
                LogTextTruncated(
                    logger, referenceNumber ?? "(sin id)", field.Id, Math.Max(1, text.Length - impresos));
            }
        }

        var font = CreateFont(fontSize, field.Bold);
        var lineHeight = fontSize * 1.25;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var yTop = field.Y + i * lineHeight;
            var drawX = field.X;
            if (field.W > 0 && field.Align != FurTextAlign.Left)
            {
                var size = gfx.MeasureString(line, font);
                drawX = field.Align switch
                {
                    FurTextAlign.Center => field.X + (field.W - size.Width) / 2,
                    FurTextAlign.Right => field.X + field.W - size.Width,
                    _ => field.X,
                };
            }

            var yBaseline = yTop + fontSize * 0.82;
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(drawX, yBaseline));
        }
    }

    private static void DrawSignatureImage(
        XGraphics gfx,
        FurFieldDefinition field,
        byte[] imageBytes,
        string? sidecarText)
    {
        var fieldH = field.H > 0 ? field.H : 36;
        var fieldW = field.W > 0 ? field.W : 120;
        var imageW = Math.Min(SignatureImageMaxWidth, fieldW * 0.50);

        // HU #11016 — la firma se dibujaba con el ALTO COMPLETO del campo y sin respetar la relación de
        // aspecto: un PNG apaisado se estiraba verticalmente y se salía del espacio de firma, pisando lo
        // que hubiera encima. Se encaja dentro de (imageW × fieldH * SignatureImageMaxHeightRatio)
        // conservando la proporción y se centra verticalmente en el campo.
        var (drawW, drawH) = FitInBox(imageBytes, imageW, fieldH * SignatureImageMaxHeightRatio);
        var imageY = field.Y + Math.Max(0, (fieldH - drawH) / 2);

        DrawImage(gfx, field.X, imageY, drawW, drawH, imageBytes);

        if (string.IsNullOrWhiteSpace(sidecarText))
            return;

        var sidecarX = field.X + imageW + SignatureSidecarGap;
        var sidecarW = Math.Max(0, fieldW - imageW - SignatureSidecarGap);
        if (sidecarW <= 0)
            return;

        DrawSidecarText(gfx, sidecarX, field.Y, sidecarW, fieldH, sidecarText);
    }

    private static void DrawSidecarText(
        XGraphics gfx,
        double x,
        double y,
        double w,
        double h,
        string text)
    {
        var font = CreateFont(SignatureSidecarFontSize, bold: false);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lineHeight = SignatureSidecarFontSize * 1.15;
        var maxLines = Math.Max(1, (int)Math.Floor(h / lineHeight));

        for (var i = 0; i < Math.Min(lines.Length, maxLines); i++)
        {
            var line = lines[i];
            if (w > 0)
            {
                line = TruncateToWidth(gfx, line, font, w);
            }

            var yBaseline = y + i * lineHeight + SignatureSidecarFontSize * 0.82;
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(x, yBaseline));
        }
    }

    private static string TruncateToWidth(XGraphics gfx, string line, XFont font, double maxWidth)
    {
        if (gfx.MeasureString(line, font).Width <= maxWidth)
            return line;

        var ellipsis = "…";
        var trimmed = line;
        while (trimmed.Length > 1 && gfx.MeasureString(trimmed + ellipsis, font).Width > maxWidth)
            trimmed = trimmed[..^1];

        return trimmed + ellipsis;
    }

    private static void DrawImage(XGraphics gfx, FurFieldDefinition field, byte[] imageBytes)
    {
        var h = field.H > 0 ? field.H : 36;
        var w = field.W > 0 ? field.W : 120;
        DrawImage(gfx, field.X, field.Y, w, h, imageBytes);
    }

    /// <summary>
    /// Mide la imagen y delega la geometría en <see cref="FurSignatureLayout.Fit"/> (HU #11016). Si la
    /// imagen no se puede leer se cae al tamaño de la caja: el dibujo posterior fallará igual, pero el
    /// cálculo no revienta la generación del FUR.
    /// </summary>
    private static (double Width, double Height) FitInBox(byte[] imageBytes, double maxW, double maxH)
    {
        try
        {
            using var ms = new MemoryStream(imageBytes);
            using var img = XImage.FromStream(() => ms);
            return FurSignatureLayout.Fit(img.PixelWidth, img.PixelHeight, maxW, maxH);
        }
        catch (Exception)
        {
            return (maxW, maxH);
        }
    }

    private static void DrawImage(XGraphics gfx, double x, double y, double w, double h, byte[] imageBytes)
    {
        using var ms = new MemoryStream(imageBytes);
        using var img = XImage.FromStream(() => ms);
        img.Interpolate = true;
        gfx.DrawImage(img, x, y, w, h);
    }

    private static XFont CreateFont(double size, bool bold)
    {
        var style = bold ? XFontStyle.Bold : XFontStyle.Regular;
        return new XFont("Arial", size, style);
    }

    // HU #11256 (R4), generalizado HU sin ADO 2026-08-11 (cuarta tanda) — último recurso de
    // FurTextFitter.FitMultiline: el texto no cupo ni al piso de cuerpo y se truncó con elipsis. Se
    // deja constancia del trámite y de cuánto se elidió; nunca se dibuja fuera de la caja. Antes solo
    // lo disparaba `observations`; desde la HU #11643 también los campos `text` con auto-encaje, entre
    // ellos `linked_company_name` (casilla 19), de ahí el nombre genérico "texto".
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "FUR {ReferenceNumber}: texto truncado en el campo {FieldId} — {ElidedChars} caracteres elididos")]
    private static partial void LogTextTruncated(ILogger logger, string referenceNumber, string fieldId, int elidedChars);
}

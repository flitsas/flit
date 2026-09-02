using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

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
        if (value.SignatureStamps is { Count: > 1 } stamps)
        {
            DrawSignatureStamps(gfx, field, stamps, logger, referenceNumber);
            return;
        }

        if (value.ImageBytes is { Length: > 0 })
        {
            DrawSignatureImage(gfx, field, value.ImageBytes, value.ImageSidecarText);
            return;
        }

        switch (field.Type)
        {
            case FurFieldType.Checkbox:
                if (string.Equals(value.Text, "X", StringComparison.OrdinalIgnoreCase))
                    DrawCheckbox(gfx, field, value.CheckboxRepeat);
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

    private static void DrawCheckbox(XGraphics gfx, FurFieldDefinition field, int repeat = 1)
    {
        var n = Math.Clamp(repeat, 1, 4);
        var single = field.FontSize > 0 ? field.FontSize + 2 : 10;
        var (fontSize, firstBaseline, step) = FurCheckboxLayout.Stack(field.Y, field.Size, n, single);
        var font = CreateFont(fontSize, true);
        for (var i = 0; i < n; i++)
            gfx.DrawString("X", font, XBrushes.Black, new XPoint(field.X, firstBaseline + i * step));
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

        if (text.Contains('\n') && field.Type != FurFieldType.Multiline)
        {
            // Copropiedad en casillas `text`: un renglón por dueño, sin el wrap horizontal de Fit.
            lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        else if (field.Type == FurFieldType.Multiline)
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
        var lineHeight = fontSize * (lines.Length >= 3 ? 1.12 : 1.25);
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

    private static void DrawSignatureStamps(
        XGraphics gfx,
        FurFieldDefinition field,
        IReadOnlyList<FurOverlaySignatureStamp> stamps,
        ILogger logger,
        string? referenceNumber)
    {
        var fieldH = field.H > 0 ? field.H : 36;
        var fieldW = field.W > 0 ? field.W : 120;
        var cols = FurSignatureLayout.Columns(field.X, fieldW, stamps.Count);
        for (var i = 0; i < cols.Length; i++)
        {
            var (colX, colW) = cols[i];
            var stamp = stamps[i];
            var colField = new FurFieldDefinition
            {
                Id = field.Id,
                Page = field.Page,
                Type = field.Type,
                X = colX,
                Y = field.Y,
                W = colW,
                H = fieldH,
                Size = field.Size,
                FontSize = field.FontSize,
                Bold = field.Bold,
                Align = FurTextAlign.Left,
            };

            if (stamp.ImageBytes is { Length: > 0 })
            {
                DrawSignatureImage(gfx, colField, stamp.ImageBytes, stamp.ImageSidecarText);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(stamp.Text))
                DrawText(gfx, colField, stamp.Text!, stamp.FontSizeDelta, logger, referenceNumber);
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
        var imageW = FurSignatureLayout.ImageWidthCap(fieldW, SignatureImageMaxWidth);

        // HU #11016 — la firma se dibujaba con el ALTO COMPLETO del campo y sin respetar la relación de
        // aspecto: un PNG apaisado se estiraba verticalmente y se salía del espacio de firma, pisando lo
        // que hubiera encima. Se encaja dentro de (imageW × fieldH * SignatureImageMaxHeightRatio)
        // conservando la proporción y se centra verticalmente en el campo.
        double drawW;
        double drawH;
        try
        {
            (drawW, drawH) = FitInBox(imageBytes, imageW, fieldH * SignatureImageMaxHeightRatio);
            var (imageY, _, _) = FurSignatureLayout.Place(field.X, field.Y, fieldW, fieldH, drawW, drawH);
            DrawImage(gfx, field.X, imageY, drawW, drawH, imageBytes);
        }
        catch (Exception)
        {
            if (!string.IsNullOrWhiteSpace(sidecarText))
                DrawSidecarText(gfx, field.X, field.Y, fieldW, fieldH, sidecarText, field.Align);
            return;
        }

        if (string.IsNullOrWhiteSpace(sidecarText))
            return;

        var (_, sidecarX, sidecarW) = FurSignatureLayout.Place(field.X, field.Y, fieldW, fieldH, drawW, drawH);
        if (sidecarW <= 0)
            return;

        DrawSidecarText(gfx, sidecarX, field.Y, sidecarW, fieldH, sidecarText, field.Align);
    }

    private static void DrawSidecarText(
        XGraphics gfx,
        double x,
        double y,
        double w,
        double h,
        string text,
        FurTextAlign align = FurTextAlign.Left)
    {
        var font = CreateFont(SignatureSidecarFontSize, bold: false);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lineHeight = SignatureSidecarFontSize * 1.15;
        var maxLines = Math.Max(1, (int)Math.Floor(h / lineHeight));
        var visible = Math.Min(lines.Length, maxLines);
        var inset = align == FurTextAlign.Center ? 2.0 : 0.0;
        var boxX = x + inset;
        var boxW = Math.Max(0, w - inset * 2);
        var boxY = y + inset;
        var boxH = Math.Max(0, h - inset * 2);
        var blockH = visible * lineHeight;
        if (align == FurTextAlign.Center && blockH < boxH)
            boxY += (boxH - blockH) / 2;

        for (var i = 0; i < visible; i++)
        {
            var line = lines[i];
            if (boxW > 0)
            {
                line = TruncateToWidth(gfx, line, font, boxW);
            }

            var drawX = boxX;
            if (align == FurTextAlign.Center && boxW > 0)
            {
                var size = gfx.MeasureString(line, font);
                drawX = boxX + Math.Max(0, (boxW - size.Width) / 2);
            }

            var yBaseline = boxY + i * lineHeight + SignatureSidecarFontSize * 0.82;
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(drawX, yBaseline));
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
            using var ms = new MemoryStream(FlattenAlphaOntoWhite(imageBytes));
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
        var payload = FlattenAlphaOntoWhite(imageBytes);
        using var ms = new MemoryStream(payload);
        using var img = XImage.FromStream(() => ms);
        img.Interpolate = true;
        gfx.DrawImage(img, x, y, w, h);
    }

    /// <summary>
    /// PdfSharpCore pinta mal (o tira) PNG con alpha. El recorte Kyverum va con fondo transparente;
    /// se aplana sobre blanco, que es el color del recuadro del FUR.
    /// </summary>
    private static byte[] FlattenAlphaOntoWhite(byte[] imageBytes)
    {
        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);
            var hasAlpha = false;
            for (var y = 0; y < image.Height && !hasAlpha; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    if (image[x, y].A < 255)
                    {
                        hasAlpha = true;
                        break;
                    }
                }
            }

            if (!hasAlpha)
                return imageBytes;

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var p = image[x, y];
                    var a = p.A / 255f;
                    image[x, y] = new Rgba32(
                        (byte)Math.Round(p.R * a + 255 * (1 - a)),
                        (byte)Math.Round(p.G * a + 255 * (1 - a)),
                        (byte)Math.Round(p.B * a + 255 * (1 - a)),
                        255);
                }
            }

            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder { ColorType = PngColorType.Rgb });
            return ms.ToArray();
        }
        catch (Exception)
        {
            return imageBytes;
        }
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

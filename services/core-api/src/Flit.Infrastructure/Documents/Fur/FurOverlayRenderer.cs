using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>Superpone valores sobre plantillas PDF blank con PdfSharpCore.</summary>
public static class FurOverlayRenderer
{
    public static byte[] RenderPage1(byte[] templatePdf, FurFieldManifest manifest, IReadOnlyDictionary<string, FurFieldValue> values)
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
            DrawField(gfx, field, value);
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
        FurFieldValue value)
    {
        if (value.ImageBytes is { Length: > 0 })
        {
            DrawImage(gfx, field, value.ImageBytes);
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
                    DrawText(gfx, field, value.Text!);
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
        string text)
    {
        var font = CreateFont(field.FontSize, field.Bold);
        var lines = field.Type == FurFieldType.Multiline
            ? text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [text];

        var lineHeight = field.FontSize * 1.25;
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

            var yBaseline = yTop + field.FontSize * 0.82;
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(drawX, yBaseline));
        }
    }

    private static void DrawImage(XGraphics gfx, FurFieldDefinition field, byte[] imageBytes)
    {
        using var ms = new MemoryStream(imageBytes);
        using var img = XImage.FromStream(() => ms);
        var h = field.H > 0 ? field.H : 36;
        var w = field.W > 0 ? field.W : 120;
        gfx.DrawImage(img, field.X, field.Y, w, h);
    }

    private static XFont CreateFont(double size, bool bold)
    {
        var style = bold ? XFontStyle.Bold : XFontStyle.Regular;
        return new XFont("Arial", size, style);
    }
}

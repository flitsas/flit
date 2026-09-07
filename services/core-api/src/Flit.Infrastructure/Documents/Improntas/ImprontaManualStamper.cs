using System.Security.Cryptography;
using System.Text;
using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Flit.Infrastructure.Documents.Improntas;

/// <summary>
/// Estampa zonas 1–3 de sellos FLIT sobre impronta cargada a mano (PdfSharpCore).
/// Zona 1 en la primera página; zonas 2–3 + pie en una página nueva al final (nunca pisa calcos).
/// </summary>
public sealed class ImprontaManualStamper : IImprontaManualStamper
{
    private static readonly XColor Blue = XColor.FromArgb(0x00, 0x55, 0xA5);
    private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

    public bool AlreadyStamped(byte[] pdf)
    {
        if (pdf is null || pdf.Length == 0)
            return false;

        try
        {
            using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
            if (string.Equals(doc.Info.Keywords, IImprontaManualStamper.MetadataKeyword, StringComparison.Ordinal))
                return true;
            if (doc.Info.Keywords?.Contains(IImprontaManualStamper.MetadataKeyword, StringComparison.Ordinal) == true)
                return true;
        }
        catch
        {
            // Fallback a búsqueda en bytes crudos.
        }

        var asLatin1 = Latin1.GetString(pdf);
        if (asLatin1.Contains(IImprontaManualStamper.MetadataKeyword, StringComparison.Ordinal)
            || asLatin1.Contains(IImprontaManualStamper.Marker, StringComparison.Ordinal))
            return true;
        var asUtf8 = Encoding.UTF8.GetString(pdf);
        return asUtf8.Contains(IImprontaManualStamper.MetadataKeyword, StringComparison.Ordinal)
            || asUtf8.Contains(IImprontaManualStamper.Marker, StringComparison.Ordinal);
    }

    public byte[] Stamp(byte[] pdf, ImprontaManualStampContext context)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(context);
        if (pdf.Length == 0)
            return pdf;
        if (AlreadyStamped(pdf))
            return pdf;

        var documentHash = Sha256Hex(pdf);
        var hashImpronta = Guid.NewGuid().ToString("D");
        var firmaDigital = BuildFirmaDigital(documentHash, hashImpronta, context);

        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
        document.Info.Keywords = IImprontaManualStamper.MetadataKeyword;

        // Zona 1 — hash del documento base en cabecera de la primera página.
        if (document.PageCount > 0)
            DrawZone1(document.Pages[0], documentHash);

        // Zonas 2–3 + pie — página dedicada para no pisar contenido previo.
        var stampPage = document.AddPage();
        if (document.PageCount > 1)
        {
            stampPage.Width = document.Pages[0].Width;
            stampPage.Height = document.Pages[0].Height;
        }

        DrawStampPage(stampPage, context, hashImpronta, firmaDigital, documentHash);

        using var ms = new MemoryStream();
        document.Save(ms, false);
        // Keyword + DocHash en comentario ASCII al final: los content streams van comprimidos
        // y no son fiables para buscar el marcador visual.
        return AppendAsciiMarker(ms.ToArray(), documentHash);
    }

    private static byte[] AppendAsciiMarker(byte[] pdf, string documentHash)
    {
        var marker = Encoding.ASCII.GetBytes(
            $"\n% {IImprontaManualStamper.MetadataKeyword}\n% DocHash:{documentHash}\n");
        var result = new byte[pdf.Length + marker.Length];
        Buffer.BlockCopy(pdf, 0, result, 0, pdf.Length);
        Buffer.BlockCopy(marker, 0, result, pdf.Length, marker.Length);
        return result;
    }

    private static void DrawZone1(PdfPage page, string documentHash)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var font = new XFont("Arial", 7, XFontStyle.Regular);
        var text = $"Identificador del documento (Hash): {documentHash}";
        gfx.DrawString(text, font, XBrushes.Black, new XRect(24, 10, page.Width - 48, 14), XStringFormats.TopLeft);
    }

    private static void DrawStampPage(
        PdfPage page,
        ImprontaManualStampContext context,
        string hashImpronta,
        string firmaDigital,
        string documentHash)
    {
        using var gfx = XGraphics.FromPdfPage(page);
        var titleFont = new XFont("Arial", 8, XFontStyle.Bold);
        var metaFont = new XFont("Arial", 6.5, XFontStyle.Regular);
        var blueBrush = new XSolidBrush(Blue);

        // Sello de tiempo vertical (margen derecho).
        var stampTime = context.FechaCargue.ToLocalTime().ToString("M/d/yyyy h:mm:ss tt");
        DrawVerticalText(gfx, $"Sello de tiempo: {stampTime}", metaFont, blueBrush, page.Width - 18, page.Height - 40);

        var signers = context.Signers.Count == 0
            ? [new ImprontaManualSigner("(sin propietario)", null, null, null)]
            : context.Signers.Take(4).ToList();

        var leftWidth = page.Width * 0.58;
        var rightX = leftWidth + 12;
        var rightWidth = page.Width - rightX - 28;
        var topY = 40.0;
        var cols = FurSignatureLayout.Columns(24, leftWidth - 24, signers.Count);

        for (var i = 0; i < signers.Count; i++)
        {
            var (colX, colW) = cols[i];
            DrawSignerBlock(gfx, signers[i], context, hashImpronta, colX, topY, colW, titleFont, metaFont, blueBrush);
        }

        // Zona 3 — firma digital del documento.
        var z3Y = topY;
        gfx.DrawString(IImprontaManualStamper.Marker, titleFont, blueBrush, new XPoint(rightX, z3Y));
        z3Y += 12;
        foreach (var line in Wrap(firmaDigital, 42))
        {
            gfx.DrawString(line, metaFont, XBrushes.Black, new XPoint(rightX, z3Y));
            z3Y += 9;
            if (z3Y > page.Height - 60)
                break;
        }

        // Pie de validez.
        var footerY = page.Height - 36;
        var footer = "Este documento cuenta con validaciones y certificaciones de seguridad válidas para los nodos de control de Runt, Simit, Hqrunt";
        gfx.DrawString(footer, metaFont, XBrushes.Black, new XRect(24, footerY, page.Width - 48, 20), XStringFormats.TopLeft);
        gfx.DrawString("Improntas del cliente", metaFont, XBrushes.Black,
            new XRect(24, footerY + 12, page.Width - 48, 14), XStringFormats.TopRight);

        // Eco del hash base (trazabilidad; también en comentario ASCII al pie del archivo).
        gfx.DrawString($"DocHash:{documentHash[..Math.Min(16, documentHash.Length)]}…", metaFont, XBrushes.Gray,
            new XPoint(24, 24));
    }

    private static void DrawSignerBlock(
        XGraphics gfx,
        ImprontaManualSigner signer,
        ImprontaManualStampContext context,
        string hashImpronta,
        double x,
        double y,
        double width,
        XFont titleFont,
        XFont metaFont,
        XBrush blueBrush)
    {
        var cursor = y;
        if (signer.SignatureImage is { Length: > 0 })
        {
            try
            {
                using var img = XImage.FromStream(() => new MemoryStream(signer.SignatureImage));
                var maxW = Math.Min(90, width - 8);
                var maxH = 36.0;
                var (dw, dh) = FurSignatureLayout.Fit(img.PixelWidth, img.PixelHeight, maxW, maxH);
                gfx.DrawImage(img, x, cursor, dw, dh);
                cursor += dh + 2;
            }
            catch
            {
                // Sin imagen usable: solo texto.
            }
        }

        gfx.DrawString("Firmado digitalmente por:", titleFont, blueBrush, new XPoint(x, cursor));
        cursor += 10;
        gfx.DrawString(Trunc(signer.FullName, 40), metaFont, XBrushes.Black, new XPoint(x, cursor));
        cursor += 10;

        if (!string.IsNullOrWhiteSpace(signer.HuellaDigital))
        {
            gfx.DrawString($"Huella digital: {Trunc(signer.HuellaDigital!, 36)}", metaFont, XBrushes.Black, new XPoint(x, cursor));
            cursor += 9;
        }

        var fecha = context.FechaCargue.ToLocalTime().ToString("dd-MM-yyyy HH:mm:ss");
        var lines = new[]
        {
            $"Hash propietario: {Trunc(signer.HashPropietario ?? "-", 36)}",
            $"Hash Impronta: {hashImpronta}",
            $"Fecha y Hora de Cargue: {fecha}",
            $"Placa del vehículo: {context.Placa ?? "-"}",
            $"Id del proceso: {context.ReferenceNumber}",
            $"NroVIN: {context.Vin ?? "-"} - NroMotor: {context.NumMotor ?? "-"} - NroChasis: {context.NumChasis ?? "-"}",
        };
        foreach (var line in lines)
        {
            foreach (var wrapped in Wrap(line, Math.Max(28, (int)(width / 4.2))))
            {
                gfx.DrawString(wrapped, metaFont, XBrushes.Black, new XPoint(x, cursor));
                cursor += 8;
            }
        }
    }

    private static void DrawVerticalText(XGraphics gfx, string text, XFont font, XBrush brush, double x, double y)
    {
        var state = gfx.Save();
        gfx.TranslateTransform(x, y);
        gfx.RotateTransform(-90);
        gfx.DrawString(text, font, brush, new XPoint(0, 0));
        gfx.Restore(state);
    }

    private static string BuildFirmaDigital(string documentHash, string hashImpronta, ImprontaManualStampContext context)
    {
        var payload = $"{documentHash}|{hashImpronta}|{context.ReferenceNumber}|{context.Placa}|{context.FechaCargue:O}";
        foreach (var s in context.Signers)
            payload += $"|{s.FullName}|{s.HashPropietario}";
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes("flit-impronta-manual-v1"), Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(mac) + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static IEnumerable<string> Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
            yield break;
        for (var i = 0; i < text.Length; i += width)
            yield return text.Substring(i, Math.Min(width, text.Length - i));
    }

    private static string Trunc(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}

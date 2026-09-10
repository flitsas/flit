using System.Security.Cryptography;
using System.Text;
using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Flit.Infrastructure.Documents.Improntas;

/// <summary>
/// Estampa zonas 1–3 sobre impronta manual. Rúbrica como el FUR (PNG + sidecar).
/// Bloque de firmas anclado al pie de la última página (sin hoja nueva).
/// </summary>
public sealed class ImprontaManualStamper : IImprontaManualStamper
{
    private static readonly XColor Blue = XColor.FromArgb(0x00, 0x55, 0xA5);
    private static readonly XColor LightGrey = XColor.FromArgb(0xB0, 0xB0, 0xB0);
    private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");
    // 50% del tamaño FUR previo (field 48→24, ancho máx. 145→72.5).
    private const double SignatureFieldH = 24;
    private const double SignatureImageMaxWidth = 72.5;
    // Base 2.5 (50% FUR) + 2pt a pedido de legibilidad de la estampa.
    private const double SignatureSidecarFontSize = 4.5;
    private const double FooterLineH = 14;
    private const double BottomPad = 8;
    private const double GapAboveFooter = 4;

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

    public ImprontaManualStampResult Stamp(byte[] pdf, ImprontaManualStampContext context)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(context);
        if (pdf.Length == 0)
            return new ImprontaManualStampResult(pdf, Applied: false);
        if (AlreadyStamped(pdf))
            return new ImprontaManualStampResult(pdf, Applied: false);

        var documentHash = Sha256Hex(pdf);
        var hashImpronta = Guid.NewGuid().ToString("D");
        // Paridad BackCrudTransfer: RSA-2048 efímero → Base64 + PEM (wrap 50 en zona 3).
        var crypto = BuildFirmaDigital(documentHash);
        var withoutOwner = context.Signers.Count == 0
            || context.Signers.All(s => s.SignatureImage is not { Length: > 0 });
        var signedAt = DateTimeOffset.UtcNow;

        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
        document.Info.Keywords = IImprontaManualStamper.MetadataKeyword;

        if (document.PageCount == 0)
            return new ImprontaManualStampResult(pdf, Applied: false);

        var page = document.Pages[document.PageCount - 1];
        DrawAllZones(page, context, hashImpronta, crypto.SignatureBase64, documentHash);

        using var ms = new MemoryStream();
        document.Save(ms, false);
        var stamped = AppendAsciiMarker(ms.ToArray(), documentHash);
        return new ImprontaManualStampResult(
            stamped,
            Applied: true,
            documentHash,
            crypto.SignatureBase64,
            crypto.PrivateKeyPem,
            crypto.PublicKeyPem,
            signedAt,
            withoutOwner);
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

    private static void DrawAllZones(
        PdfPage page,
        ImprontaManualStampContext context,
        string hashImpronta,
        string firmaDigital,
        string documentHash)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var titleFont = new XFont("Arial", 8, XFontStyle.Bold);
        var metaFont = new XFont("Arial", 6.5, XFontStyle.Regular);
        var greyBrush = new XSolidBrush(LightGrey);
        var blueBrush = new XSolidBrush(Blue);

        // Zona 1 — hash del documento base en cabecera (fuera de la sección de firma).
        gfx.DrawString(
            $"Identificador del documento (Hash): {documentHash}",
            new XFont("Arial", 7, XFontStyle.Regular),
            XBrushes.Black,
            new XRect(24, 10, page.Width - 48, 14),
            XStringFormats.TopLeft);

        var stampTime = context.FechaCargue.ToLocalTime().ToString("M/d/yyyy h:mm:ss tt");
        DrawVerticalText(gfx, $"Sello de tiempo: {stampTime}", metaFont, blueBrush, page.Width - 18, page.Height - 40);

        var signers = context.Signers.Count == 0
            ? [new ImprontaManualSigner("(sin propietario)", null, null)]
            : context.Signers.Take(4).ToList();

        // Pie: frase de validez (gris). Sin "Improntas del cliente".
        var footerY = page.Height - BottomPad - FooterLineH;
        gfx.DrawString(
            "Este documento cuenta con validaciones y certificaciones de seguridad válidas para los nodos de control de Runt, Simit, Hqrunt",
            metaFont, greyBrush,
            new XRect(24, footerY, page.Width - 48, FooterLineH), XStringFormats.TopLeft);

        // Bloque de firmas alineado al final de la página (justo encima del footer).
        var multiOwner = signers.Count > 1;
        var compactLayout = signers.Count >= 3;
        var sigFieldH = compactLayout ? 20.0 : SignatureFieldH;
        var leftBlockH = EstimateLeftBlockHeight(signers.Count, multiOwner, sigFieldH);
        var rightBlockH = EstimateRightBlockHeight(firmaDigital);
        var contentH = Math.Max(leftBlockH, rightBlockH);
        var topY = Math.Max(28.0, footerY - GapAboveFooter - contentH);

        var leftWidth = page.Width * 0.58;
        var rightX = leftWidth + 12;
        var cols = FurSignatureLayout.Columns(24, leftWidth - 24, signers.Count);
        var fourActor = signers.Count == 4;

        for (var i = 0; i < signers.Count; i++)
        {
            var (colX, colW) = cols[i];
            DrawSignerColumn(
                gfx, signers[i], context, hashImpronta,
                colX, topY, colW, fourActor, compactLayout, sigFieldH,
                signerIndex: i, signers.Count, metaFont, greyBrush);
        }

        if (multiOwner)
        {
            // Hashes apilados en vertical (no uno por columna) + metas compartidas.
            var cursor = topY + EstimateRubricRowHeight(sigFieldH);
            DrawOwnerHashesVertical(
                gfx, signers, 24, ref cursor, leftWidth - 24, metaFont, greyBrush);
            DrawSharedMetadata(
                gfx, context, hashImpronta, 24, cursor, leftWidth - 24, metaFont, greyBrush);
        }

        // Zona 3 — título azul; cuerpo Base64 RSA (legacy wrap 50).
        var z3Y = topY;
        gfx.DrawString(IImprontaManualStamper.Marker, titleFont, blueBrush, new XPoint(rightX, z3Y + 8));
        z3Y += 16;
        foreach (var line in Wrap(firmaDigital, FirmaDigitalWrapWidth))
        {
            gfx.DrawString(line, metaFont, XBrushes.Black, new XPoint(rightX, z3Y));
            z3Y += 8;
            if (z3Y > footerY - 2)
                break;
        }
    }

    private const int FirmaDigitalWrapWidth = 50;

    /// <summary>Solo la banda de rúbricas (multi-propietario: sin hash en columna).</summary>
    private static double EstimateRubricRowHeight(double sigFieldH) =>
        sigFieldH + 4;

    /// <summary>Cada hash completo puede ocupar ~2 líneas a ancho del bloque izquierdo.</summary>
    private static double EstimateOwnerHashesHeight(int signerCount) =>
        Math.Max(1, signerCount) * (2 * 8);

    private static double EstimateSharedMetadataHeight() =>
        4 + (1 * 8) + (4 * 8);

    internal static string FormatOwnerHashLabel(int signerIndex, int signerCount, string? hashPropietario) =>
        signerCount > 1
            ? $"Hash propietario{signerIndex + 1}: {hashPropietario ?? "-"}"
            : $"Hash propietario: {hashPropietario ?? "-"}";

    internal static bool UsesSharedMetadataBlock(int signerCount) => signerCount > 1;

    internal static bool StacksOwnerHashesVertically(int signerCount) => signerCount > 1;

    internal static double EstimateLayoutHeightForTest(int signerCount)
    {
        var multi = signerCount > 1;
        var sigFieldH = signerCount >= 3 ? 20.0 : SignatureFieldH;
        return EstimateLeftBlockHeight(signerCount, multi, sigFieldH);
    }

    internal static double EstimateLegacyRepeatedHeightForTest(int signerCount)
    {
        var sigFieldH = signerCount >= 3 ? 20.0 : SignatureFieldH;
        var perColumn = sigFieldH + 4 + (2 * 8) + EstimateSharedMetadataHeight();
        return perColumn * signerCount;
    }

    private static double EstimateLeftBlockHeight(int signerCount, bool multiOwner, double sigFieldH)
    {
        if (!multiOwner)
        {
            // Rúbrica + hash propietario (puede ir en 2 líneas) + metas compartidas.
            return sigFieldH + 4 + (2 * 8) + EstimateSharedMetadataHeight();
        }

        // Fila de rúbricas + lista vertical de hashes + metas una sola vez.
        return EstimateRubricRowHeight(sigFieldH)
               + EstimateOwnerHashesHeight(signerCount)
               + EstimateSharedMetadataHeight()
               + 2;
    }

    private static double EstimateRightBlockHeight(string firmaDigital) =>
        16 + (Math.Ceiling(Math.Max(1, firmaDigital.Length) / (double)FirmaDigitalWrapWidth) * 8) + 2;

    private static void DrawSignerColumn(
        XGraphics gfx,
        ImprontaManualSigner signer,
        ImprontaManualStampContext context,
        string hashImpronta,
        double x,
        double y,
        double width,
        bool fourActorLayout,
        bool compactLayout,
        double sigFieldH,
        int signerIndex,
        int totalSigners,
        XFont metaFont,
        XBrush greyBrush)
    {
        var multiOwner = totalSigners > 1;
        var cursor = y;

        // Rúbrica (PNG + sidecar); sin título "Firmado digitalmente por".
        var drew = DrawFurStyleSignature(gfx, signer, x, cursor, width, sigFieldH, fourActorLayout, greyBrush);
        cursor += (drew ? sigFieldH : Math.Min(sigFieldH, 16)) + 4;

        // Multi: hashes van apilados debajo de la fila (DrawOwnerHashesVertical). Uno: hash + metas aquí.
        if (multiOwner)
            return;

        var hashWrap = Math.Max(compactLayout ? 32 : 48, (int)(width / 3.2));
        var hashLabel = FormatOwnerHashLabel(signerIndex, totalSigners, signer.HashPropietario);
        DrawWrappedLines(gfx, metaFont, greyBrush, x, ref cursor, hashLabel, hashWrap);
        DrawSharedMetadata(gfx, context, hashImpronta, x, cursor, width, metaFont, greyBrush);
    }

    private static void DrawOwnerHashesVertical(
        XGraphics gfx,
        List<ImprontaManualSigner> signers,
        double x,
        ref double cursor,
        double width,
        XFont metaFont,
        XBrush greyBrush)
    {
        var hashWrap = Math.Max(48, (int)(width / 3.2));
        for (var i = 0; i < signers.Count; i++)
        {
            var label = FormatOwnerHashLabel(i, signers.Count, signers[i].HashPropietario);
            DrawWrappedLines(gfx, metaFont, greyBrush, x, ref cursor, label, hashWrap);
        }
    }

    private static void DrawSharedMetadata(
        XGraphics gfx,
        ImprontaManualStampContext context,
        string hashImpronta,
        double x,
        double y,
        double width,
        XFont metaFont,
        XBrush greyBrush)
    {
        var cursor = y;
        var fecha = context.FechaCargue.ToLocalTime().ToString("dd-MM-yyyy HH:mm:ss");
        var hashWrap = Math.Max(48, (int)(width / 3.2));
        var metaWrap = Math.Max(28, (int)(width / 4.2));
        DrawWrappedLines(gfx, metaFont, greyBrush, x, ref cursor,
            $"Hash Impronta: {hashImpronta}", hashWrap);
        foreach (var line in new[]
                 {
                     $"Fecha y Hora de Cargue: {fecha}",
                     $"Placa del vehículo: {context.Placa ?? "-"}",
                     $"Id del proceso: {context.ReferenceNumber}",
                     $"NroVIN: {context.Vin ?? "-"} - NroMotor: {context.NumMotor ?? "-"} - NroChasis: {context.NumChasis ?? "-"}",
                 })
        {
            DrawWrappedLines(gfx, metaFont, greyBrush, x, ref cursor, line, metaWrap);
        }
    }

    private static void DrawWrappedLines(
        XGraphics gfx, XFont font, XBrush brush, double x, ref double cursor, string text, int wrapWidth)
    {
        foreach (var wrapped in Wrap(text, wrapWidth))
        {
            gfx.DrawString(wrapped, font, brush, new XPoint(x, cursor + 6));
            cursor += 8;
        }
    }

    /// <returns><c>true</c> si pintó imagen o sello en la banda de rúbrica.</returns>
    private static bool DrawFurStyleSignature(
        XGraphics gfx,
        ImprontaManualSigner signer,
        double fieldX,
        double fieldY,
        double fieldW,
        double fieldH,
        bool fourActorLayout,
        XBrush greyBrush)
    {
        if (signer.SignatureImage is { Length: > 0 })
        {
            try
            {
                var payload = FlattenAlphaOntoWhite(signer.SignatureImage);
                using var img = XImage.FromStream(() => new MemoryStream(payload));
                img.Interpolate = true;

                var imageW = FurSignatureLayout.ImageWidthCap(fieldW, SignatureImageMaxWidth, fourActorLayout);
                var (dw, dh) = FurSignatureLayout.Fit(
                    img.PixelWidth, img.PixelHeight, imageW, fieldH * 0.88);
                if (dw > 0 && dh > 0)
                {
                    var (imageY, sidecarX, sidecarW) = FurSignatureLayout.Place(
                        fieldX, fieldY, fieldW, fieldH, dw, dh, fourActorLayout);
                    gfx.DrawImage(img, fieldX, Math.Max(fieldY, imageY), dw, dh);

                    if (!string.IsNullOrWhiteSpace(signer.ImageSidecarText) && sidecarW > 0)
                    {
                        DrawSidecarText(
                            gfx, sidecarX, fieldY, sidecarW, fieldH,
                            signer.ImageSidecarText!,
                            SignatureSidecarFontSize,
                            greyBrush);
                    }

                    return true;
                }
            }
            catch
            {
                // Cae a sello texto.
            }
        }

        var seal = !string.IsNullOrWhiteSpace(signer.SealText)
            ? signer.SealText
            : signer.ImageSidecarText;
        if (!string.IsNullOrWhiteSpace(seal))
        {
            DrawSidecarText(gfx, fieldX, fieldY, fieldW, fieldH, seal!, fontSize: 3.5, greyBrush);
            return true;
        }

        return false;
    }

    private static void DrawSidecarText(
        XGraphics gfx, double x, double y, double w, double h, string text, double fontSize, XBrush brush)
    {
        var font = new XFont("Arial", fontSize, XFontStyle.Regular);
        var lineH = fontSize * 1.15;
        var cursor = y + fontSize;
        foreach (var raw in text.Split('\n'))
        {
            if (cursor > y + h)
                break;
            var line = TruncToWidth(gfx, font, raw, w);
            gfx.DrawString(line, font, brush, new XPoint(x, cursor));
            cursor += lineH;
        }
    }

    private static string TruncToWidth(XGraphics gfx, XFont font, string text, double maxW)
    {
        if (string.IsNullOrEmpty(text) || gfx.MeasureString(text, font).Width <= maxW)
            return text;
        var ellipsis = "…";
        for (var len = text.Length - 1; len > 0; len--)
        {
            var candidate = text[..len] + ellipsis;
            if (gfx.MeasureString(candidate, font).Width <= maxW)
                return candidate;
        }

        return ellipsis;
    }

    private static byte[] FlattenAlphaOntoWhite(byte[] imageBytes)
    {
        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);
            var hasAlpha = false;
            for (var row = 0; row < image.Height && !hasAlpha; row++)
            {
                for (var col = 0; col < image.Width; col++)
                {
                    if (image[col, row].A < 255)
                    {
                        hasAlpha = true;
                        break;
                    }
                }
            }

            if (!hasAlpha)
                return imageBytes;

            for (var row = 0; row < image.Height; row++)
            {
                for (var col = 0; col < image.Width; col++)
                {
                    var p = image[col, row];
                    var a = p.A / 255f;
                    image[col, row] = new Rgba32(
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
        catch
        {
            return imageBytes;
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

    internal readonly record struct FirmaDigitalMaterial(
        string SignatureBase64,
        string PrivateKeyPem,
        string PublicKeyPem);

    /// <summary>
    /// Paridad con BackCrudTransfer <c>VehicleTransferSignedPdfService.signDocument</c>:
    /// RSA-2048 efímero, firma SHA-256 del hash del documento (UTF-8), Base64 + PEM.
    /// </summary>
    internal static FirmaDigitalMaterial BuildFirmaDigital(string documentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentHash);
        using var rsa = RSA.Create(2048);
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(documentHash),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return new FirmaDigitalMaterial(
            Convert.ToBase64String(signature),
            rsa.ExportRSAPrivateKeyPem(),
            rsa.ExportRSAPublicKeyPem());
    }

    /// <summary>
    /// Verifica la firma RSA-SHA256 PKCS#1 del hash del documento (HU #12148). Solo clave pública.
    /// </summary>
    public static bool VerifyFirmaDigital(string publicKeyPem, string documentHash, string signatureBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureBase64);

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            var signature = Convert.FromBase64String(signatureBase64);
            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(documentHash),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
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

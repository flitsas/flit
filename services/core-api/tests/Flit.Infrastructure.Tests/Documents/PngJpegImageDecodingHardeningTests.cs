using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Documents.Fur;
using Flit.Infrastructure.Documents.Improntas;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #13371 (épica #13216) — hallazgo L2 del security-agent sobre el residual de ImageSharp 2.1.11:
/// (1) toda decodificación pasa por la cabecera con tope de píxeles (bomba de descompresión);
/// (2) PdfSharpCore (<c>XImage.FromStream</c>) nunca recibe los bytes originales del usuario, sino un PNG
/// recodificado por ImageSharp; (3) ni ICC ni metadatos (iCCP, tEXt, APP2) sobreviven a la recodificación.
/// Uso de ejemplo:
/// <code>
/// var clean = PngJpegImageDecoding.ReencodeToCleanPng(bytes); // null si no es PNG/JPEG o supera el tope
/// using var img = XImage.FromStream(() => new MemoryStream(clean!));
/// </code>
/// </summary>
public sealed class PngJpegImageDecodingHardeningTests
{
    private const string IccMarker = "FLITICCMARK";
    private const string TextMarker = "FLITMETAMARK";

    /// <summary>Por encima del tope (30M &gt; 25M) pero asumible si el código viejo llegara a decodificarla.</summary>
    private const int OverW = 6000;
    private const int OverH = 5000;

    /// <summary>Reservar una imagen fuera del tope son ≥ 120 MB; el camino acotado no llega ni a la mitad.</summary>
    private const long MaxAllocatedBytes = 64L * 1024 * 1024;

    private readonly ITestOutputHelper _output;

    public PngJpegImageDecodingHardeningTests(ITestOutputHelper output) => _output = output;

    // ---- Fixtures ---------------------------------------------------------------------------------

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(
            chunk.AsSpan(8 + data.Length), Crc32(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    /// <summary>Inserta chunks justo después de IHDR (firma 8 + IHDR 25 = 33 bytes).</summary>
    private static byte[] InsertAfterIhdr(byte[] png, params byte[][] chunks)
    {
        using var ms = new MemoryStream();
        ms.Write(png, 0, 33);
        foreach (var c in chunks)
            ms.Write(c);
        ms.Write(png, 33, png.Length - 33);
        return ms.ToArray();
    }

    /// <summary>PNG legítimo de 32x16 cuya cabecera IHDR declara otras dimensiones (CRC recalculado).</summary>
    internal static byte[] PngDeclaring(int width, int height)
    {
        var png = PngJpegImageDecodingTests.Png();
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29), Crc32(png.AsSpan(12, 17)));
        return png;
    }

    /// <summary>JPEG legítimo de 32x16 cuyo SOF declara otras dimensiones.</summary>
    internal static byte[] JpegDeclaring(int width, int height)
    {
        var jpeg = PngJpegImageDecodingTests.Jpeg();
        for (var i = 2; i < jpeg.Length - 9; i++)
        {
            if (jpeg[i] == 0xFF && jpeg[i + 1] is 0xC0 or 0xC1 or 0xC2)
            {
                BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(i + 5), (ushort)height);
                BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(i + 7), (ushort)width);
                return jpeg;
            }
        }

        throw new InvalidOperationException("La fixture JPEG no tiene SOF.");
    }

    /// <summary>Perfil ICC mínimo (cabecera de 128 bytes + 0 etiquetas) con un marcador legible al final.</summary>
    private static byte[] MinimalIccProfile()
    {
        var marker = Encoding.ASCII.GetBytes(IccMarker);
        var icc = new byte[132 + marker.Length];
        BinaryPrimitives.WriteInt32BigEndian(icc, icc.Length);
        BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(8), 0x02100000); // versión 2.1
        Encoding.ASCII.GetBytes("mntr").CopyTo(icc, 12);
        Encoding.ASCII.GetBytes("RGB ").CopyTo(icc, 16);
        Encoding.ASCII.GetBytes("XYZ ").CopyTo(icc, 20);
        Encoding.ASCII.GetBytes("acsp").CopyTo(icc, 36);
        marker.CopyTo(icc, 132);
        return icc;
    }

    /// <summary>PNG 32x16 con chunk iCCP (nombre = marcador) y tEXt con otro marcador.</summary>
    private static byte[] PngWithIccAndText()
    {
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true))
            zs.Write(MinimalIccProfile());
        var iccp = Encoding.ASCII.GetBytes(IccMarker + "\0\0").Concat(z.ToArray()).ToArray();
        var text = Encoding.ASCII.GetBytes("Comment\0" + TextMarker);
        return InsertAfterIhdr(PngJpegImageDecodingTests.Png(), PngChunk("iCCP", iccp), PngChunk("tEXt", text));
    }

    /// <summary>JPEG 32x16 con segmento APP2 ICC_PROFILE (una sola parte) justo después de SOI.</summary>
    private static byte[] JpegWithIcc()
    {
        var jpeg = PngJpegImageDecodingTests.Jpeg();
        var icc = MinimalIccProfile();
        var payload = Encoding.ASCII.GetBytes("ICC_PROFILE\0").Concat(new byte[] { 1, 1 }).Concat(icc).ToArray();
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE2;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);
        return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray();
    }

    /// <summary>PNG RGBA con un píxel semitransparente (para comprobar que el alfa se conserva).</summary>
    private static byte[] PngWithAlpha()
    {
        using var image = new Image<Rgba32>(40, 20, new Rgba32(0, 0, 0, 0));
        image[5, 5] = new Rgba32(10, 20, 30, 128);
        image[6, 5] = new Rgba32(200, 100, 50, 255);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>JPEG «fotográfico» determinista (degradado + ruido), del tamaño de una foto de firma.</summary>
    private static byte[] PhotoLikeJpeg(int width, int height)
    {
        var rnd = new Random(13371);
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var n = rnd.Next(-24, 25);
                image[x, y] = new Rgba32(
                    (byte)Math.Clamp((x * 255 / width) + n, 0, 255),
                    (byte)Math.Clamp((y * 255 / height) + n, 0, 255),
                    (byte)Math.Clamp(128 + n, 0, 255),
                    255);
            }
        }

        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static List<string> PngChunkTypes(byte[] png)
    {
        var types = new List<string>();
        var pos = 8;
        while (pos + 8 <= png.Length)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            types.Add(Encoding.ASCII.GetString(png, pos + 4, 4));
            pos += 12 + len;
        }

        return types;
    }

    private static bool Contains(byte[] haystack, string ascii) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(ascii)) >= 0;

    private static int CountImageXObjects(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return doc.Internals.GetAllObjects()
            .OfType<PdfDictionary>()
            .Count(d => d.Elements.GetName("/Subtype") == "/Image");
    }

    private static byte[] BlankPdf()
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private static ImprontaManualStampContext ImprontaCtx(params ImprontaManualSigner[] signers) =>
        new(
            ReferenceNumber: "FLIT-0000001",
            Placa: "AAA000",
            Vin: "VIN0000000000000",
            NumMotor: "MOTOR0",
            NumChasis: "CHASIS0",
            FechaCargue: new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.FromHours(-5)),
            Signers: signers);

    private static byte[] StampImpronta(byte[] image) =>
        new ImprontaManualStamper()
            .Stamp(BlankPdf(), ImprontaCtx(new ImprontaManualSigner("ANA", "h1", image, SealText: "SELLO")))
            .Pdf;

    private static byte[] RenderFurSignature(byte[] image)
    {
        var manifest = FurFieldManifestLoader.LoadEmbedded(FurTemplateFormat.Automotor);
        var values = new Dictionary<string, FurFieldValue>
        {
            ["vehicle_owner_signature"] = new(null, ImageBytes: image, ImageSidecarText: "SELLO"),
        };
        return FurOverlayRenderer.RenderPage1(BlankPdf(), manifest, values);
    }

    private static long AllocatedDuring(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // ---- 1. Tope de píxeles -----------------------------------------------------------------------

    [Fact]
    public void Fixtures_DimensionesGigantes_LaCabeceraLasDeclara()
    {
        PngJpegImageDecoding.Identify(PngDeclaring(30_000, 30_000))!.Width.Should().Be(30_000);
        PngJpegImageDecoding.Identify(JpegDeclaring(30_000, 30_000))!.Height.Should().Be(30_000);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void Helper_CabeceraDe30000x30000_NoSeDecodifica(string format)
    {
        var bytes = format == "png" ? PngDeclaring(30_000, 30_000) : JpegDeclaring(30_000, 30_000);

        PngJpegImageDecoding.LoadBounded(bytes, PngJpegImageDecoding.MaxDecodePixels).Should().BeNull();
        PngJpegImageDecoding.ReencodeToCleanPng(bytes).Should().BeNull();
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void Helper_Load_FueraDelTope_LanzaSinReservarPixeles(string format)
    {
        var bytes = format == "png" ? PngDeclaring(OverW, OverH) : JpegDeclaring(OverW, OverH);
        Exception? thrown = null;

        var allocated = AllocatedDuring(() =>
        {
            try
            {
                using var _ = PngJpegImageDecoding.Load(bytes);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });

        thrown.Should().BeAssignableTo<ImageFormatException>("fuera del tope se trata como imagen inválida");
        allocated.Should().BeLessThan(MaxAllocatedBytes);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void Impronta_FirmaFueraDelTope_CaeAlSelloSinDecodificar(string format)
    {
        var bytes = format == "png" ? PngDeclaring(OverW, OverH) : JpegDeclaring(OverW, OverH);
        ImprontaManualStampResult? result = null;

        var allocated = AllocatedDuring(() => result = new ImprontaManualStamper()
            .Stamp(BlankPdf(), ImprontaCtx(new ImprontaManualSigner("ANA", "h1", bytes, SealText: "SELLO"))));

        result!.Applied.Should().BeTrue();
        CountImageXObjects(result.Pdf).Should().Be(0, "fuera del tope la firma es inválida: queda el sello de texto");
        allocated.Should().BeLessThan(MaxAllocatedBytes);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void Fur_FirmaFueraDelTope_SoloQuedaElSelloSinDecodificar(string format)
    {
        var bytes = format == "png" ? PngDeclaring(OverW, OverH) : JpegDeclaring(OverW, OverH);
        byte[]? pdf = null;

        var allocated = AllocatedDuring(() => pdf = RenderFurSignature(bytes));

        CountImageXObjects(pdf!).Should().Be(0, "fuera del tope la firma es inválida: queda el sello de texto");
        allocated.Should().BeLessThan(MaxAllocatedBytes);
    }

    // ---- 2. Metadatos e ICC ------------------------------------------------------------------------

    [Fact]
    public void Fixture_JpegConApp2Icc_ElDecodificadorPorDefectoLeeElPerfil()
    {
        using var image = Image.Load<Rgba32>(JpegWithIcc());

        image.Metadata.IccProfile.Should().NotBeNull("la fixture debe llevar un ICC que ImageSharp por defecto sí lee");
    }

    [Fact]
    public void PngJpegOnly_NoLeeMetadatosNiIcc()
    {
        using var jpeg = Image.Load<Rgba32>(PngJpegImageDecoding.PngJpegOnly, JpegWithIcc());
        jpeg.Metadata.IccProfile.Should().BeNull();
        jpeg.Metadata.ExifProfile.Should().BeNull();

        using var png = Image.Load<Rgba32>(PngJpegImageDecoding.PngJpegOnly, PngWithIccAndText());
        png.Metadata.IccProfile.Should().BeNull();
        png.Metadata.GetPngMetadata().TextData.Should().BeEmpty();
    }

    [Fact]
    public void Reencode_PngConIccpYTexto_SaleSoloConIhdrIdatIend()
    {
        var original = PngWithIccAndText();
        Contains(original, "iCCP").Should().BeTrue();

        var clean = PngJpegImageDecoding.ReencodeToCleanPng(original);

        clean.Should().NotBeNull();
        IdentitySignatureImageFormatIsPng(clean!).Should().BeTrue();
        PngChunkTypes(clean!).Distinct().Should().BeEquivalentTo("IHDR", "IDAT", "IEND");
        Contains(clean!, IccMarker).Should().BeFalse();
        Contains(clean!, TextMarker).Should().BeFalse();
        using var decoded = Image.Load<Rgba32>(clean!);
        decoded.Width.Should().Be(32);
        decoded.Height.Should().Be(16);
    }

    [Fact]
    public void Reencode_JpegConApp2Icc_SalePngSinIcc()
    {
        var original = JpegWithIcc();
        Contains(original, IccMarker).Should().BeTrue();

        var clean = PngJpegImageDecoding.ReencodeToCleanPng(original);

        clean.Should().NotBeNull();
        IdentitySignatureImageFormatIsPng(clean!).Should().BeTrue();
        PngChunkTypes(clean!).Distinct().Should().BeEquivalentTo("IHDR", "IDAT", "IEND");
        Contains(clean!, IccMarker).Should().BeFalse();
        using var decoded = Image.Load<Rgba32>(clean!);
        decoded.Metadata.IccProfile.Should().BeNull();
        decoded.Width.Should().Be(32);
        decoded.Height.Should().Be(16);
    }

    [Fact]
    public void Reencode_ConservaAlfaYDimensiones()
    {
        var clean = PngJpegImageDecoding.ReencodeToCleanPng(PngWithAlpha());

        clean.Should().NotBeNull();
        using var decoded = Image.Load<Rgba32>(clean!);
        decoded.Width.Should().Be(40);
        decoded.Height.Should().Be(20);
        decoded[5, 5].Should().Be(new Rgba32(10, 20, 30, 128));
        decoded[6, 5].Should().Be(new Rgba32(200, 100, 50, 255));
        decoded[0, 0].A.Should().Be(0);
    }

    [Theory]
    [InlineData("tiff")]
    [InlineData("vacio")]
    public void Reencode_NoPngJpeg_DevuelveNull(string kind)
    {
        var bytes = kind == "tiff" ? PngJpegImageDecodingTests.MinimalTiff() : [];

        PngJpegImageDecoding.ReencodeToCleanPng(bytes).Should().BeNull();
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void Impronta_FirmaConIcc_PdfSeGeneraSinIccNiBytesOriginales(string format)
    {
        var pdf = StampImpronta(format == "png" ? PngWithIccAndText() : JpegWithIcc());

        CountImageXObjects(pdf).Should().BeGreaterThan(0);
        Contains(pdf, "ICCBased").Should().BeFalse();
        Contains(pdf, IccMarker).Should().BeFalse();
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void Fur_FirmaConIcc_PdfSeGeneraSinIccNiBytesOriginales(string format)
    {
        var pdf = RenderFurSignature(format == "png" ? PngWithIccAndText() : JpegWithIcc());

        CountImageXObjects(pdf).Should().BeGreaterThan(0);
        Contains(pdf, "ICCBased").Should().BeFalse();
        Contains(pdf, IccMarker).Should().BeFalse();
    }

    // ---- 3. XImage recibe el PNG recodificado, no los bytes originales -----------------------------

    /// <summary>
    /// PdfSharpCore incrusta un JPEG como /DCTDecode y un PNG como /FlateDecode. Si la firma JPEG del
    /// usuario sale como /DCTDecode es que <c>XImage.FromStream</c> recibió el JPEG original.
    /// </summary>
    [Fact]
    public void Impronta_FirmaJpeg_XImageRecibePngRecodificado()
    {
        var pdf = StampImpronta(PngJpegImageDecodingTests.Jpeg(64, 32));

        CountImageXObjects(pdf).Should().BeGreaterThan(0);
        Contains(pdf, "/DCTDecode").Should().BeFalse("XImage debe recibir el PNG recodificado, no el JPEG original");
    }

    [Fact]
    public void Fur_FirmaJpeg_XImageRecibePngRecodificado()
    {
        var pdf = RenderFurSignature(PngJpegImageDecodingTests.Jpeg(64, 32));

        CountImageXObjects(pdf).Should().BeGreaterThan(0);
        Contains(pdf, "/DCTDecode").Should().BeFalse("XImage debe recibir el PNG recodificado, no el JPEG original");
    }

    [Fact]
    public void Fur_FirmaPngConAlfa_SigueAplanandoSobreBlanco()
    {
        var pdf = RenderFurSignature(PngWithAlpha());

        CountImageXObjects(pdf).Should().Be(1, "el alfa se aplana sobre blanco: sin /SMask aparte");
    }

    // ---- Medición: tamaño del PDF con una firma JPEG fotográfica --------------------------------------

    [Fact]
    public void Medicion_TamanoPdf_FirmaJpegFotografica()
    {
        var jpeg = PhotoLikeJpeg(1280, 960);

        var fur = RenderFurSignature(jpeg);
        var impronta = StampImpronta(jpeg);

        // Antes del cambio una firma JPEG opaca llegaba tal cual a XImage (sin aplanar): se incrustaba
        // como /DCTDecode. Se compara esa incrustación con la del PNG recodificado, mismo lienzo.
        var antes = EmbedInBlankPdf(jpeg);
        var despues = EmbedInBlankPdf(PngJpegImageDecoding.RequireCleanPng(jpeg));
        _output.WriteLine($"JPEG entrada 1280x960: {jpeg.Length} bytes");
        _output.WriteLine($"PDF con XImage del JPEG original (antes): {antes.Length} bytes");
        _output.WriteLine($"PDF con XImage del PNG recodificado (después): {despues.Length} bytes");
        var firma = PngJpegImageDecodingTests.Png(672, 270);
        _output.WriteLine(
            $"Firma PNG 672x270: antes {EmbedInBlankPdf(firma).Length} bytes; después {EmbedInBlankPdf(PngJpegImageDecoding.RequireCleanPng(firma)).Length} bytes");
        _output.WriteLine($"FUR con la firma: {fur.Length} bytes; sin firma: {RenderFurSignature([]).Length} bytes");
        _output.WriteLine($"Impronta con la firma: {impronta.Length} bytes");
        CountImageXObjects(fur).Should().BeGreaterThan(0);
        CountImageXObjects(impronta).Should().BeGreaterThan(0);
    }

    private static byte[] EmbedInBlankPdf(byte[] image)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page))
        using (var img = PdfSharpCore.Drawing.XImage.FromStream(() => new MemoryStream(image)))
        {
            gfx.DrawImage(img, 10, 10, 200, 150);
        }

        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private static bool IdentitySignatureImageFormatIsPng(byte[] bytes) =>
        Flit.Tramites.Application.Identity.IdentitySignatureImageFormat.IsPng(bytes);
}

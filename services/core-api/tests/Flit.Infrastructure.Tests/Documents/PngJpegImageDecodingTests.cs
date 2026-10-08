using System.Buffers.Binary;
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
/// HU #13371 (épica #13216) — SixLabors.ImageSharp 2.1.11 (fijado por PdfSharpCore) tiene cinco avisos
/// abiertos (GHSA-gwg2-r3hj-4w44, GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q, GHSA-wmxv-xphr-5c9g,
/// GHSA-jjfr-hcj7-qf5w), varios en el códec TIFF. Toda decodificación con ImageSharp se limita a PNG/JPEG:
/// un TIFF (o BigTIFF) válido no llega al decodificador y cada sitio se comporta como ante una imagen inválida.
/// Uso de ejemplo:
/// <code>
/// using var image = PngJpegImageDecoding.LoadBounded(bytes, maxPixels: 4_000_000); // null si no es PNG/JPEG
/// PngJpegImageDecoding.EnsureAllowed(bytes); // antes de XImage.FromStream
/// </code>
/// </summary>
public sealed class PngJpegImageDecodingTests
{
    // ---- Fixtures ---------------------------------------------------------------------------------

    /// <summary>TIFF little-endian mínimo y válido: 16x16, gris 8 bits, sin compresión, una tira.</summary>
    internal static byte[] MinimalTiff(int width = 16, int height = 16)
    {
        const int entries = 9;
        const int ifdOffset = 8;
        const int ifdSize = 2 + (entries * 12) + 4;
        const int dataOffset = ifdOffset + ifdSize;
        var pixels = width * height;
        var bytes = new byte[dataOffset + pixels];
        bytes[0] = (byte)'I';
        bytes[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), ifdOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(ifdOffset), entries);

        var cursor = ifdOffset + 2;
        void Entry(ushort tag, ushort type, uint value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(cursor), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(cursor + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 4), 1);
            if (type == 3)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(cursor + 8), (ushort)value);
            else
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 8), value);
            cursor += 12;
        }

        Entry(256, 3, (uint)width); // ImageWidth
        Entry(257, 3, (uint)height); // ImageLength
        Entry(258, 3, 8); // BitsPerSample
        Entry(259, 3, 1); // Compression = none
        Entry(262, 3, 1); // PhotometricInterpretation = BlackIsZero
        Entry(273, 4, dataOffset); // StripOffsets
        Entry(277, 3, 1); // SamplesPerPixel
        Entry(278, 3, (uint)height); // RowsPerStrip
        Entry(279, 4, (uint)pixels); // StripByteCounts
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0); // sin más IFD

        for (var i = 0; i < pixels; i++)
            bytes[dataOffset + i] = (byte)(i % 2 == 0 ? 20 : 230);
        return bytes;
    }

    /// <summary>Cabecera BigTIFF (<c>II+\0</c>, versión 43) con un IFD vacío de 8 bytes.</summary>
    private static byte[] MinimalBigTiffHeader()
    {
        var bytes = new byte[32];
        bytes[0] = (byte)'I';
        bytes[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 43);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 16);
        return bytes;
    }

    internal static byte[] Png(int width = 32, int height = 16)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(10, 10, 10, 255));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    internal static byte[] Jpeg(int width = 32, int height = 16)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(10, 10, 10, 255));
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

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

    // ---- Control: la fixture TIFF sí la decodifica ImageSharp con la configuración por defecto --------

    [Fact]
    public void Fixture_Tiff_EsTiffValidoParaImageSharpPorDefecto()
    {
        var info = Image.Identify(MinimalTiff());

        info.Should().NotBeNull("la fixture debe ser un TIFF que el decodificador por defecto sí abriría");
        info!.Width.Should().Be(16);
        using var decoded = Image.Load<Rgba32>(MinimalTiff());
        decoded.Height.Should().Be(16);
    }

    // ---- Helper único ---------------------------------------------------------------------------------

    [Fact]
    public void PngJpegOnly_SoloRegistraPngYJpeg()
    {
        PngJpegImageDecoding.PngJpegOnly.ImageFormats
            .Select(f => f.Name)
            .Should().BeEquivalentTo("PNG", "JPEG");
    }

    [Fact]
    public void Tiff_NoSeIdentificaNiSeDecodifica()
    {
        var tiff = MinimalTiff();

        PngJpegImageDecoding.IsAllowed(tiff).Should().BeFalse();
        PngJpegImageDecoding.Identify(tiff).Should().BeNull();
        PngJpegImageDecoding.LoadBounded(tiff, 4_000_000).Should().BeNull();
        var load = () => PngJpegImageDecoding.Load(tiff);
        load.Should().Throw<UnknownImageFormatException>();
        var ensure = () => PngJpegImageDecoding.EnsureAllowed(tiff);
        ensure.Should().Throw<UnknownImageFormatException>();
    }

    [Fact]
    public void BigTiff_SeRechazaPorCabecera()
    {
        var bigTiff = MinimalBigTiffHeader();

        PngJpegImageDecoding.IsAllowed(bigTiff).Should().BeFalse();
        PngJpegImageDecoding.Identify(bigTiff).Should().BeNull();
        PngJpegImageDecoding.LoadBounded(bigTiff, 4_000_000).Should().BeNull();
    }

    [Fact]
    public void NullOVacio_SeTratanComoNoAdmitidos()
    {
        PngJpegImageDecoding.IsAllowed(null).Should().BeFalse();
        PngJpegImageDecoding.Identify([]).Should().BeNull();
        PngJpegImageDecoding.LoadBounded(null, 4_000_000).Should().BeNull();
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void PngYJpeg_SeIdentificanYSeDecodifican(string format)
    {
        var bytes = format == "png" ? Png() : Jpeg();

        PngJpegImageDecoding.IsAllowed(bytes).Should().BeTrue();
        var info = PngJpegImageDecoding.Identify(bytes);
        info.Should().NotBeNull();
        info!.Width.Should().Be(32);
        using var bounded = PngJpegImageDecoding.LoadBounded(bytes, 4_000_000);
        bounded.Should().NotBeNull();
        bounded!.Height.Should().Be(16);
        using var loaded = PngJpegImageDecoding.Load(bytes);
        loaded.Width.Should().Be(32);
    }

    [Fact]
    public void LoadBounded_SuperaTopeDePixeles_DevuelveNull()
    {
        PngJpegImageDecoding.LoadBounded(Png(32, 16), maxPixels: 100).Should().BeNull();
    }

    // ---- Sitio 1: ImprontaManualStamper (imagen subida por el usuario) ----------------------------------

    [Fact]
    public void Impronta_FirmaTiff_NoSeDecodifica_CaeAlSelloDeTexto()
    {
        var sut = new ImprontaManualStamper();
        var ctx = ImprontaCtx(new ImprontaManualSigner("ANA", "h1", MinimalTiff(), SealText: "SELLO"));

        var result = sut.Stamp(BlankPdf(), ctx);

        result.Applied.Should().BeTrue();
        CountImageXObjects(result.Pdf).Should().Be(0, "un TIFF no debe llegar a XImage.FromStream / ImageSharp");
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void Impronta_FirmaPngOJpeg_SeIncrustaComoImagen(string format)
    {
        var sut = new ImprontaManualStamper();
        var bytes = format == "png" ? Png() : Jpeg();
        var ctx = ImprontaCtx(new ImprontaManualSigner("ANA", "h1", bytes, SealText: "SELLO"));

        var result = sut.Stamp(BlankPdf(), ctx);

        result.Applied.Should().BeTrue();
        CountImageXObjects(result.Pdf).Should().BeGreaterThan(0);
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

    // ---- Sitio 2: FurOverlayRenderer (firma del FUR) ----------------------------------------------------

    [Fact]
    public void Fur_FirmaTiff_NoSeDecodifica_SoloQuedaElSello()
    {
        var pdf = RenderFurSignature(MinimalTiff());

        CountImageXObjects(pdf).Should().Be(0, "un TIFF no debe llegar a XImage.FromStream / ImageSharp");
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void Fur_FirmaPngOJpeg_SeIncrustaComoImagen(string format)
    {
        var pdf = RenderFurSignature(format == "png" ? Png() : Jpeg());

        CountImageXObjects(pdf).Should().BeGreaterThan(0);
    }

    private static byte[] RenderFurSignature(byte[] image)
    {
        var manifest = FurFieldManifestLoader.LoadEmbedded(FurTemplateFormat.Automotor);
        var values = new Dictionary<string, FurFieldValue>
        {
            ["vehicle_owner_signature"] = new(null, ImageBytes: image, ImageSidecarText: "SELLO"),
        };
        return FurOverlayRenderer.RenderPage1(BlankPdf(), manifest, values);
    }

    // ---- Sitio 3: IdentitySignatureExtractor.TryMeasure --------------------------------------------------

    [Fact]
    public void Extractor_TryMeasure_Tiff_DevuelveFalse()
    {
        IdentitySignatureExtractor.TryMeasure(MinimalTiff(), out var w, out var h).Should().BeFalse();
        w.Should().Be(0);
        h.Should().Be(0);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void Extractor_TryMeasure_PngOJpeg_DevuelveDimensiones(string format)
    {
        var bytes = format == "png" ? Png() : Jpeg();

        IdentitySignatureExtractor.TryMeasure(bytes, out var w, out var h).Should().BeTrue();
        w.Should().Be(32);
        h.Should().Be(16);
    }

    // ---- Sitio 4: PdfXObjectPngDecoder (ya acotado; no-regresión) ----------------------------------------

    [Fact]
    public void Decoder_Tiff_SeTrataComoImagenInvalida()
    {
        var tiff = MinimalTiff();

        PdfXObjectPngDecoder.ToDocumentInk(tiff).Should().BeSameAs(tiff);
        PdfXObjectPngDecoder.CountVisibleColors(tiff, 16).Should().Be(int.MaxValue);
        PdfXObjectPngDecoder.LooksLikeSignatureArtifact(tiff).Should().BeFalse();
        PdfXObjectPngDecoder.HasVisibleInk(tiff).Should().BeFalse();
    }

    [Fact]
    public void Decoder_Png_SigueDecodificando()
    {
        PdfXObjectPngDecoder.CountVisibleColors(Png(), 16).Should().Be(1);
        PdfXObjectPngDecoder.CountVisibleColors(Jpeg(), 16).Should().BeLessThan(int.MaxValue);
    }
}

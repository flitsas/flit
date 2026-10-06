using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Filters;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

public sealed class IdentitySignatureExtractorTests
{
    private static byte[] ScribblePng()
    {
        using var image = new Image<Rgba32>(48, 16);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
                image[x, y] = x % 3 == 0 ? new Rgba32(0, 0, 0) : new Rgba32(255, 255, 255);
        }

        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    [Fact]
    public void PdfVacio_DevuelveNull()
    {
        new IdentitySignatureExtractor().TryExtract([]).Should().BeNull();
        new IdentitySignatureExtractor().TryExtract("%PDF-1.4 not a real file"u8.ToArray()).Should().BeNull();
    }

    [Fact]
    public void PdfConXObjectImagen_ExtraeBytes()
    {
        var png = ScribblePng();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        using (var img = XImage.FromStream(() => new MemoryStream(png)))
        {
            gfx.DrawString(
                "FIRMA Y AUTORIZACION DE TRAMITE DIGITAL",
                new XFont("Arial", 10),
                XBrushes.Black,
                40,
                40);
            gfx.DrawImage(img, 40, 80, 80, 24);
        }

        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        var crop = new IdentitySignatureExtractor().TryExtract(ms.ToArray());

        crop.Should().NotBeNull();
        IdentitySignatureImageFormat.IsPng(crop!.PngBytes).Should().BeTrue();
        using var decoded = Image.Load(crop.PngBytes);
        decoded.Width.Should().BeGreaterThan(0);
        decoded.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PdfConRastersFlateDeviceRgb_ExtraePngValido()
    {
        var width = 64;
        var height = 20;
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                var ink = x > 8 && x < 56 && y > 6 && y < 14;
                rgb[i] = rgb[i + 1] = rgb[i + 2] = ink ? (byte)20 : (byte)255;
            }
        }

        var pdf = BuildPdfWithFlateRgbImage(width, height, rgb);
        var crop = new IdentitySignatureExtractor().TryExtract(pdf);

        crop.Should().NotBeNull();
        IdentitySignatureImageFormat.IsPng(crop!.PngBytes).Should().BeTrue();
        using var decoded = Image.Load(crop.PngBytes);
        decoded.Width.Should().Be(width);
        decoded.Height.Should().Be(height);
    }

    [Fact]
    public void GrisOscuroSobreNegro_PasaATintaVisibleConFondoTransparente()
    {
        using var source = new Image<Rgba32>(80, 40);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
                source[x, y] = new Rgba32(0, 0, 0);
        }

        for (var x = 8; x < 72; x++)
        {
            source[x, 18] = new Rgba32(26, 26, 26);
            source[x, 19] = new Rgba32(30, 30, 30);
            source[x, 20] = new Rgba32(22, 22, 22);
        }

        using var ms = new MemoryStream();
        source.Save(ms, new PngEncoder());
        var ink = PdfXObjectPngDecoder.ToDocumentInk(ms.ToArray());

        using var decoded = Image.Load<Rgba32>(ink);
        var transparent = 0;
        var visibleInk = 0;
        for (var y = 0; y < decoded.Height; y++)
        {
            for (var x = 0; x < decoded.Width; x++)
            {
                var p = decoded[x, y];
                if (p.A == 0)
                    transparent++;
                if (p.A >= 96 && p.R <= 40)
                    visibleInk++;
            }
        }

        transparent.Should().BeGreaterThan(decoded.Width * decoded.Height / 2);
        visibleInk.Should().BeGreaterThan(50);
        PdfXObjectPngDecoder.HasVisibleInk(ink).Should().BeTrue();
        PdfXObjectPngDecoder.HasVisibleInk(ms.ToArray()).Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------------
    // Bug #13304 — Kyverum añadió un logo de cabecera (676x200, Flate RGB, sin /SMask, color continuo)
    // que por aspecto le ganaba a la rúbrica (672x270, Flate RGB con /SMask, paleta baja). PDFs
    // sintéticos: solo imitan la estructura de imágenes, sin datos reales.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Bug13304_LayoutNuevo_LogoOpacoDeColorContinuoYRubricaConSMask_EligeLaRubrica()
    {
        var crop = new IdentitySignatureExtractor().TryExtract(BuildPdf(LayoutNuevo()));

        crop.Should().NotBeNull("la rúbrica con /SMask debe ganarle al logo opaco de cabecera");
        using var decoded = Image.Load<Rgba32>(crop!.PngBytes);
        decoded.Width.Should().Be(672, "debe elegir la rúbrica 672x270 y no el logo 676x200");
        decoded.Height.Should().Be(270);
        CountTransparent(decoded).Should().BeGreaterThan(decoded.Width * decoded.Height / 2);
    }

    [Fact]
    public void Bug13304_LayoutAnterior_RubricaConSMaskFotosYQr_SigueEligiendoLaRubrica()
    {
        var crop = new IdentitySignatureExtractor().TryExtract(BuildPdf(LayoutAnterior()));

        crop.Should().NotBeNull();
        using var decoded = Image.Load<Rgba32>(crop!.PngBytes);
        decoded.Width.Should().Be(792);
        decoded.Height.Should().Be(360);
    }

    [Fact]
    public void Bug13304_SinMascaras_LaPaletaBajaLeGanaAlLogoDeColorContinuo()
    {
        var rubric = KyverumRubric(672, 270, out _);
        var pdf = BuildPdf(
            new PdfImageSpec("/ImLogo", 676, 200, LogoRgb(676, 200), "/DeviceRGB", "/FlateDecode"),
            new PdfImageSpec("/ImSig", 672, 270, rubric, "/DeviceRGB", "/FlateDecode"));

        var crop = new IdentitySignatureExtractor().TryExtract(pdf);

        crop.Should().NotBeNull();
        using var decoded = Image.Load<Rgba32>(crop!.PngBytes);
        decoded.Width.Should().Be(672, "sin /SMask, la paleta baja desempata antes que el aspecto");
    }

    [Fact]
    public void Bug13304_IsUsableInk_LogoPersistidoOpacoDeColorContinuo_EsFalso()
    {
        new IdentitySignatureExtractor().IsUsableInk(LogoPng()).Should().BeFalse(
            "el logo «Verify» guardado por error no es una rúbrica y debe re-extraerse");
    }

    [Fact]
    public void Bug13304_IsUsableInk_RecorteDeLaRubrica_EsVerdadero()
    {
        var extractor = new IdentitySignatureExtractor();
        var crop = extractor.TryExtract(BuildPdf(LayoutAnterior()));

        crop.Should().NotBeNull();
        extractor.IsUsableInk(crop!.PngBytes).Should().BeTrue();
    }

    [Fact]
    public void Bug13304_TintaOscuraSobreFondoClaro_SaleConFondoTransparente()
    {
        using var source = new Image<Rgba32>(200, 60, new Rgba32(255, 255, 255));
        for (var x = 10; x < 190; x++)
        {
            source[x, 29] = new Rgba32(20, 20, 20);
            source[x, 30] = new Rgba32(20, 20, 20);
            source[x, 31] = new Rgba32(20, 20, 20);
        }

        using var ms = new MemoryStream();
        source.Save(ms, new PngEncoder());
        var ink = PdfXObjectPngDecoder.ToDocumentInk(ms.ToArray());

        using var decoded = Image.Load<Rgba32>(ink);
        CountTransparent(decoded).Should().BeGreaterThan(decoded.Width * decoded.Height / 2,
            "todo recorte de rúbrica debe llevar canal alfa para distinguirlo del logo opaco");
        new IdentitySignatureExtractor().IsUsableInk(ink).Should().BeTrue();
    }

    [Fact]
    public async Task Bug13304_Autocorreccion_LogoPersistido_RecapturaLaRubricaYSobrescribe()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sut, store) = CaptureWithRealExtractor();
        var v = KyverumValidation("s3://logo");
        store.OpenReadAsync("s3://logo", Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(LogoPng()));
        byte[]? saved = null;
        store.SaveAsync(v.TenantId, Arg.Do<byte[]>(b => saved = b), Arg.Any<CancellationToken>())
            .Returns(new StoredIdentitySignature("s3://rubrica", "beef"));

        var outcome = await sut.EnsureFromPdfAsync(v, BuildPdf(LayoutNuevo()), ct);

        outcome.Should().Be(IdentitySignatureCaptureOutcome.Captured);
        v.SignatureImagePath.Should().Be("s3://rubrica");
        v.SignatureImageSha256.Should().Be("beef");
        saved.Should().NotBeNull();
        using var decoded = Image.Load<Rgba32>(saved!);
        decoded.Width.Should().Be(672);
        decoded.Height.Should().Be(270);
    }

    [Fact]
    public async Task Bug13304_Autocorreccion_RecorteBuenoPersistido_NoRecaptura()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sut, store) = CaptureWithRealExtractor();
        var good = new IdentitySignatureExtractor().TryExtract(BuildPdf(LayoutAnterior()))!.PngBytes;
        var v = KyverumValidation("s3://bueno");
        store.OpenReadAsync("s3://bueno", Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(good));

        var outcome = await sut.EnsureFromPdfAsync(v, BuildPdf(LayoutNuevo()), ct);

        outcome.Should().Be(IdentitySignatureCaptureOutcome.AlreadyPresent);
        v.SignatureImagePath.Should().Be("s3://bueno");
        await store.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    private static (IdentitySignatureCapture Sut, IIdentitySignatureArtifactStorage Store) CaptureWithRealExtractor()
    {
        var store = Substitute.For<IIdentitySignatureArtifactStorage>();
        var sut = new IdentitySignatureCapture(
            Substitute.For<IKyverumCertificateClient>(),
            new IdentitySignatureExtractor(),
            store,
            Substitute.For<IProcedureInstanceRepository>(),
            NullLogger<IdentitySignatureCapture>.Instance);
        return (sut, store);
    }

    private static ProcedureInstanceBiometricValidation KyverumValidation(string path) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Provider = BiometricProviders.Kyverum,
        KyverumVerificationId = "kv-13304",
        Status = BiometricEstados.Aprobado,
        SignatureImagePath = path,
        SignatureImageSha256 = "old",
    };

    private static int CountTransparent(Image<Rgba32> image)
    {
        var transparent = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image[x, y].A < 128)
                    transparent++;
            }
        }

        return transparent;
    }

    /// <summary>Layout Kyverum 2026-10: logo de cabecera opaco + 3 fotos DCT + rúbrica con /SMask + QR.</summary>
    private static PdfImageSpec[] LayoutNuevo()
    {
        var rubric = KyverumRubric(672, 270, out var mask);
        var photo = PhotoJpeg(1280, 960);
        return
        [
            new PdfImageSpec("/ImLogo", 676, 200, LogoRgb(676, 200), "/DeviceRGB", "/FlateDecode"),
            new PdfImageSpec("/ImFoto1", 1280, 960, photo, "/DeviceRGB", "/DCTDecode"),
            new PdfImageSpec("/ImFoto2", 1280, 960, photo, "/DeviceRGB", "/DCTDecode"),
            new PdfImageSpec("/ImFoto3", 1280, 960, photo, "/DeviceRGB", "/DCTDecode"),
            new PdfImageSpec("/ImSig", 672, 270, rubric, "/DeviceRGB", "/FlateDecode", mask),
            new PdfImageSpec("/ImQr", 132, 132, QrGray(132), "/DeviceGray", "/FlateDecode"),
        ];
    }

    /// <summary>Layout Kyverum anterior: 3 fotos DCT + rúbrica 792x360 con /SMask + QR, sin logo-imagen.</summary>
    private static PdfImageSpec[] LayoutAnterior()
    {
        var rubric = KyverumRubric(792, 360, out var mask);
        var photo = PhotoJpeg(1280, 960);
        return
        [
            new PdfImageSpec("/ImFoto1", 1280, 960, photo, "/DeviceRGB", "/DCTDecode"),
            new PdfImageSpec("/ImFoto2", 1280, 960, photo, "/DeviceRGB", "/DCTDecode"),
            new PdfImageSpec("/ImFoto3", 1280, 960, photo, "/DeviceRGB", "/DCTDecode"),
            new PdfImageSpec("/ImSig", 792, 360, rubric, "/DeviceRGB", "/FlateDecode", mask),
            new PdfImageSpec("/ImQr", 132, 132, QrGray(132), "/DeviceGray", "/FlateDecode"),
        ];
    }

    /// <summary>Trazo gris muy oscuro sobre negro (pocos tonos) y su /SMask: 255 en el trazo, 0 fuera.</summary>
    private static byte[] KyverumRubric(int width, int height, out byte[] mask)
    {
        var rgb = new byte[width * height * 3];
        mask = new byte[width * height];
        byte[] tones = [22, 24, 26, 28, 30];
        for (var curve = 0; curve < 3; curve++)
        {
            for (var x = width / 10; x < width * 9 / 10; x++)
            {
                var cy = height / 2 + (int)(Math.Sin((x + curve * 37) / 23.0) * height / 4) + (curve - 1) * 12;
                for (var dy = -2; dy <= 2; dy++)
                {
                    var y = Math.Clamp(cy + dy, 0, height - 1);
                    var p = y * width + x;
                    var tone = tones[(x + dy + 2) % tones.Length];
                    rgb[p * 3] = rgb[p * 3 + 1] = rgb[p * 3 + 2] = tone;
                    mask[p] = 255;
                }
            }
        }

        return rgb;
    }

    /// <summary>Logo de cabecera: fondo blanco y banda central de color continuo (miles de colores).</summary>
    private static byte[] LogoRgb(int width, int height)
    {
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                var band = y >= height / 5 && y < height * 4 / 5 && x >= width / 10 && x < width * 9 / 10;
                rgb[i] = band ? (byte)(x % 256) : (byte)255;
                rgb[i + 1] = band ? (byte)(y * 3 % 256) : (byte)255;
                rgb[i + 2] = band ? (byte)(x * y % 256) : (byte)255;
            }
        }

        return rgb;
    }

    /// <summary>El mismo logo como PNG opaco: lo que quedó persistido como «rúbrica» antes del fix.</summary>
    private static byte[] LogoPng()
    {
        const int width = 676;
        const int height = 200;
        var rgb = LogoRgb(width, height);
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                image[x, y] = new Rgba32(rgb[i], rgb[i + 1], rgb[i + 2]);
            }
        }

        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder { ColorType = PngColorType.RgbWithAlpha });
        return ms.ToArray();
    }

    private static byte[] PhotoJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                image[x, y] = new Rgba32((byte)(x * 255 / width), (byte)(y * 255 / height), 128);
        }

        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = 70 });
        return ms.ToArray();
    }

    private static byte[] QrGray(int size)
    {
        var gray = new byte[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
                gray[y * size + x] = ((x / 6) + (y / 6)) % 2 == 0 ? (byte)0 : (byte)255;
        }

        return gray;
    }

    private sealed record PdfImageSpec(
        string Name,
        int Width,
        int Height,
        byte[] Data,
        string ColorSpace,
        string Filter,
        byte[]? SoftMask = null);

    private static byte[] BuildPdf(params PdfImageSpec[] images)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        var resources = page.Elements.GetDictionary("/Resources") ?? new PdfDictionary(doc);
        page.Elements["/Resources"] = resources;
        var xObjects = resources.Elements.GetDictionary("/XObject") ?? new PdfDictionary();
        resources.Elements["/XObject"] = xObjects;

        foreach (var spec in images)
        {
            var image = ImageXObject(doc, spec.Width, spec.Height, spec.ColorSpace, spec.Filter, spec.Data);
            if (spec.SoftMask is not null)
                image.Elements["/SMask"] = ImageXObject(doc, spec.Width, spec.Height, "/DeviceGray", "/FlateDecode", spec.SoftMask);
            xObjects.Elements[spec.Name] = image;
        }

        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private static PdfDictionary ImageXObject(PdfDocument doc, int width, int height, string colorSpace, string filter, byte[] data)
    {
        var image = new PdfDictionary(doc);
        image.Elements.SetName("/Type", "/XObject");
        image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetInteger("/Width", width);
        image.Elements.SetInteger("/Height", height);
        image.Elements.SetName("/ColorSpace", colorSpace);
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.Elements.SetName("/Filter", filter);
        image.CreateStream(filter == "/FlateDecode" ? new FlateDecode().Encode(data) : data);
        doc.Internals.AddObject(image);
        return image;
    }

    private static byte[] BuildPdfWithFlateRgbImage(int width, int height, byte[] rgb)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        var image = new PdfDictionary(doc);
        image.Elements.SetName("/Type", "/XObject");
        image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetInteger("/Width", width);
        image.Elements.SetInteger("/Height", height);
        image.Elements.SetName("/ColorSpace", "/DeviceRGB");
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.Elements.SetName("/Filter", "/FlateDecode");
        var compressed = new FlateDecode().Encode(rgb);
        image.CreateStream(compressed);
        doc.Internals.AddObject(image);

        var resources = page.Elements.GetDictionary("/Resources") ?? new PdfDictionary(doc);
        page.Elements["/Resources"] = resources;
        var xObjects = resources.Elements.GetDictionary("/XObject") ?? new PdfDictionary();
        resources.Elements["/XObject"] = xObjects;
        xObjects.Elements["/ImSig"] = image;

        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }
}

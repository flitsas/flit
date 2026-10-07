using Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13290 (Feature #13281 B, Épica #13202) — <see cref="ManualCaptureImages"/>: el formato se reconoce por los magic bytes,
/// no por el nombre ni el Content-Type; la firma solo admite PNG.
/// <para>Uso: <c>ManualCaptureImages.Detect(bytes)</c> y <c>IsAllowedPhoto/IsAllowedSignature(formato)</c>.</para>
/// </summary>
public sealed class ManualCaptureImagesTests
{
    [Fact]
    public void Reconoce_JPEG_PNG_y_WebP_por_contenido()
    {
        ManualCaptureImages.Detect([0xFF, 0xD8, 0xFF, 0xE1, 0x00]).Should().Be(ManualCaptureImageFormat.Jpeg);
        ManualCaptureImages.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]).Should().Be(ManualCaptureImageFormat.Png);
        ManualCaptureImages.Detect("RIFF\0\0\0\0WEBPVP8 "u8).Should().Be(ManualCaptureImageFormat.Webp);
    }

    [Fact]
    public void Rechaza_lo_que_no_es_imagen_admitida()
    {
        ManualCaptureImages.Detect([]).Should().Be(ManualCaptureImageFormat.Unknown);
        ManualCaptureImages.Detect([0xFF, 0xD8]).Should().Be(ManualCaptureImageFormat.Unknown, "muy corto");
        ManualCaptureImages.Detect("GIF89a"u8).Should().Be(ManualCaptureImageFormat.Unknown);
        ManualCaptureImages.Detect("%PDF-1.7"u8).Should().Be(ManualCaptureImageFormat.Unknown);
        ManualCaptureImages.Detect("<svg xmlns='http://www.w3.org/2000/svg'/>"u8).Should().Be(ManualCaptureImageFormat.Unknown);
        ManualCaptureImages.Detect("RIFF\0\0\0\0WAVEfmt "u8).Should().Be(ManualCaptureImageFormat.Unknown, "RIFF que no es WebP");
        ManualCaptureImages.Detect([0x89, 0x50, 0x4E, 0x47]).Should().Be(ManualCaptureImageFormat.Unknown, "PNG con firma incompleta");
    }

    [Fact]
    public void La_firma_solo_admite_PNG_y_las_fotos_JPEG_PNG_o_WebP()
    {
        ManualCaptureImages.IsAllowedSignature(ManualCaptureImageFormat.Png).Should().BeTrue();
        ManualCaptureImages.IsAllowedSignature(ManualCaptureImageFormat.Jpeg).Should().BeFalse();
        ManualCaptureImages.IsAllowedSignature(ManualCaptureImageFormat.Webp).Should().BeFalse();

        ManualCaptureImages.IsAllowedPhoto(ManualCaptureImageFormat.Jpeg).Should().BeTrue();
        ManualCaptureImages.IsAllowedPhoto(ManualCaptureImageFormat.Png).Should().BeTrue();
        ManualCaptureImages.IsAllowedPhoto(ManualCaptureImageFormat.Webp).Should().BeTrue();
        ManualCaptureImages.IsAllowedPhoto(ManualCaptureImageFormat.Unknown).Should().BeFalse();
    }

    [Fact]
    public void Los_limites_documentados()
    {
        ManualCaptureImages.MaxImageBytes.Should().Be(5 * 1024 * 1024);
        ManualCaptureImages.MaxSignatureBytes.Should().Be(2 * 1024 * 1024);
        ManualCaptureImages.MaxRequestBytes.Should().BeGreaterThan(3 * ManualCaptureImages.MaxImageBytes + ManualCaptureImages.MaxSignatureBytes);
    }
}

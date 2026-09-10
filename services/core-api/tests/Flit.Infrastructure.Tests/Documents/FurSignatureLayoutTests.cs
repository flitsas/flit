using Flit.Infrastructure.Documents.Fur;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #11016 — la firma del baúl se dibujaba con el ALTO COMPLETO del campo y sin respetar la relación
/// de aspecto, así que una firma apaisada (lo habitual en un PNG de firma) se estiraba verticalmente y
/// se salía del espacio de firma, pisando los campos vecinos del FUR.
/// </summary>
public sealed class FurSignatureLayoutTests
{
    [Fact]
    public void Fit_FirmaApaisada_ConservaProporcion_YCabeEnLaCaja()
    {
        // 600×200 (3:1) en una caja de 115×28.8 (≈4:1): manda el ALTO, porque la caja es más
        // apaisada que la firma. Antes se dibujaba 115×28.8, deformándola.
        var (w, h) = FurSignatureLayout.Fit(600, 200, 115, 28.8);

        h.Should().BeApproximately(28.8, 0.01);
        w.Should().BeApproximately(86.4, 0.01);
        (w / h).Should().BeApproximately(3, 0.01);
        w.Should().BeLessThanOrEqualTo(115);
    }

    [Fact]
    public void Fit_FirmaMuyApaisada_LimitaPorElAncho()
    {
        // 1000×100 (10:1) es más apaisada que la caja: ahí manda el ancho.
        var (w, h) = FurSignatureLayout.Fit(1000, 100, 115, 28.8);

        w.Should().BeApproximately(115, 0.01);
        h.Should().BeApproximately(11.5, 0.01);
        h.Should().BeLessThanOrEqualTo(28.8);
    }

    [Fact]
    public void Fit_FirmaVertical_LimitaPorElAlto_YNoDesbordaLaCaja()
    {
        // 200×600 (1:3): estirarla al alto del campo era justo lo que invadía otros campos.
        var (w, h) = FurSignatureLayout.Fit(200, 600, 115, 28.8);

        h.Should().BeApproximately(28.8, 0.01);
        w.Should().BeApproximately(9.6, 0.01);
        h.Should().BeLessThanOrEqualTo(28.8);
        w.Should().BeLessThanOrEqualTo(115);
    }

    [Fact]
    public void Fit_ImagenPequena_NoSeAmpliaMasAllaDeLaCaja()
    {
        var (w, h) = FurSignatureLayout.Fit(50, 20, 115, 28.8);

        w.Should().BeLessThanOrEqualTo(115);
        h.Should().BeLessThanOrEqualTo(28.8);
        (w / h).Should().BeApproximately(2.5, 0.01);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-10, 100)]
    public void Fit_MedidasInvalidas_CaeALaCaja(double imgW, double imgH)
    {
        // Imagen ilegible o degenerada: se conserva el comportamiento previo en vez de romper el FUR.
        var (w, h) = FurSignatureLayout.Fit(imgW, imgH, 115, 28.8);

        w.Should().Be(115);
        h.Should().Be(28.8);
    }

    [Fact]
    public void Place_SidecarUsaElAnchoReal_NoLaMitadReservadaDelCampo()
    {
        var (drawW, drawH) = FurSignatureLayout.Fit(646, 315, 145, 28.8);
        var (imageY, sidecarX, sidecarW) = FurSignatureLayout.Place(
            fieldX: 100, fieldY: 200, fieldW: 230, fieldH: 36, drawW, drawH);

        sidecarX.Should().BeApproximately(100 + drawW + FurSignatureLayout.SidecarGap, 0.01);
        sidecarX.Should().BeLessThan(100 + 145);
        sidecarW.Should().BeApproximately(230 - drawW - FurSignatureLayout.SidecarGap, 0.01);
        imageY.Should().BeApproximately(200 + Math.Max(0, (36 - drawH) / 2) - FurSignatureLayout.ImageLift, 0.01);
        FurSignatureLayout.ImageLift.Should().Be(2);
    }

    [Fact]
    public void Columns_FourOwners_ExpandsLeftAndWidens()
    {
        const double fieldX = 102;
        const double fieldW = 262;
        var cols = FurSignatureLayout.Columns(fieldX, fieldW, 4);

        cols.Should().HaveCount(4);
        cols[0].X.Should().BeApproximately(fieldX - FourActorSignatureLayout.ExpandLeft, 0.01);
        cols.Sum(c => c.W).Should().BeApproximately(fieldW + FourActorSignatureLayout.ExpandLeft, 0.01);
        cols[0].W.Should().BeApproximately((fieldW + FourActorSignatureLayout.ExpandLeft) / 4, 0.01);
        cols[3].X.Should().BeApproximately(cols[0].X + 3 * cols[0].W, 0.01);
    }

    [Fact]
    public void Columns_ThreeOwners_Unchanged()
    {
        const double fieldX = 102;
        const double fieldW = 262;
        var cols = FurSignatureLayout.Columns(fieldX, fieldW, 3);

        cols.Should().HaveCount(3);
        cols[0].X.Should().Be(fieldX);
        cols[0].W.Should().BeApproximately(fieldW / 3, 0.01);
        cols.Sum(c => c.W).Should().BeApproximately(fieldW, 0.01);
    }

    [Fact]
    public void ImageWidthCap_FourActor_LeavesMoreRoomForSidecarThanNarrowDefault()
    {
        const double fieldW = 78;
        var fourImageW = FurSignatureLayout.ImageWidthCap(fieldW, 145, fourActorLayout: true);
        var defaultImageW = FurSignatureLayout.ImageWidthCap(fieldW, 145);

        fourImageW.Should().BeApproximately(fieldW * FourActorSignatureLayout.ImageFraction, 0.01);
        var fourSidecarW = fieldW - fourImageW - FourActorSignatureLayout.SidecarGap;
        var defaultSidecarW = fieldW - defaultImageW - FurSignatureLayout.SidecarGap;
        fourSidecarW.Should().BeGreaterThan(defaultSidecarW);
    }

    [Fact]
    public void Place_FourActor_NoImageLift_UsesTighterSidecarGap()
    {
        var fieldW = 78;
        var imageW = FurSignatureLayout.ImageWidthCap(fieldW, 145, fourActorLayout: true);
        var (drawW, drawH) = FurSignatureLayout.Fit(600, 200, imageW, 28.8);
        var (imageY, sidecarX, sidecarW) = FurSignatureLayout.Place(
            fieldX: 52, fieldY: 381, fieldW, fieldH: 32, drawW, drawH, fourActorLayout: true);

        imageY.Should().BeApproximately(381 + Math.Max(0, (32 - drawH) / 2), 0.01);
        sidecarX.Should().BeApproximately(52 + drawW + FourActorSignatureLayout.SidecarGap, 0.01);
        sidecarW.Should().BeApproximately(fieldW - drawW - FourActorSignatureLayout.SidecarGap, 0.01);
    }

    [Fact]
    public void ImageWidthCap_NarrowColumn_LeavesRoomForSidecar()
    {
        var fieldW = 65.5;
        var imageW = FurSignatureLayout.ImageWidthCap(fieldW, 145);
        var sidecarW = fieldW - imageW - FurSignatureLayout.SidecarGap;
        sidecarW.Should().BeGreaterThan(22);
        imageW.Should().BeLessThan(fieldW * 0.45);
    }
}

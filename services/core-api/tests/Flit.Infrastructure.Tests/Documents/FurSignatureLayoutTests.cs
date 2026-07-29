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
}

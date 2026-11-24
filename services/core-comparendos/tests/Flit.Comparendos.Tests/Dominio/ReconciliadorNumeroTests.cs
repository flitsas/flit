using Flit.Comparendos.Api.Modulos.Ingesta;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Persistence.Modelo;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Dominio;

/// <summary>GDC-ASOC §8: la fuente no revierte el número sin letra de un comparendo que ya existe.</summary>
public sealed class ReconciliadorNumeroTests
{
    private const string Largo = "11001000000000012345";

    [Theory]
    [InlineData($"D-{Largo}", Largo, Largo, Largo)]
    [InlineData(Largo, $"D{Largo}", $"D{Largo}", Largo)]
    [InlineData("98765", "A-98765", "98765", "A98765")]
    [InlineData("OTRO-1", "X-9", "OTRO-1", "X9")]
    public void AplicarCampos_RespetaElNumeroGuardado_SoloSiLaLlaveEsLaMisma(string traeLaFuente, string guardado, string esperado, string llave)
    {
        var c = new Comparendo { Numero = guardado, NumeroNormalizado = llave };

        Reconciliador.AplicarCampos(c, new ItemFuente { Numero = traeLaFuente });

        c.Numero.Should().Be(esperado);
    }

    [Fact]
    public void AplicarCampos_ConUnaLlaveDistinta_SigueActualizando()
    {
        var c = new Comparendo { Numero = "1100-A", NumeroNormalizado = "1100A" };

        Reconciliador.AplicarCampos(c, new ItemFuente { Numero = "D-11001000000000099999" });

        c.Numero.Should().Be("D-11001000000000099999");
    }

    [Fact]
    public void AplicarCampos_PrimerIngreso_EscribeElNumeroDeLaFuente()
    {
        var c = new Comparendo { NumeroNormalizado = Largo };

        Reconciliador.AplicarCampos(c, new ItemFuente { Numero = $"D-{Largo}" });

        c.Numero.Should().Be($"D-{Largo}");
    }
}

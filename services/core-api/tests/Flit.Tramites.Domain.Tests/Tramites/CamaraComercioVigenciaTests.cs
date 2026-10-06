using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Tramites;

/// <summary>
/// HU #12776 — la regla de los 30 días del certificado de Cámara de Comercio. Es pura: recibe la
/// fecha y el día de hoy, así que se puede comprobar sin reloj y sin base de datos.
/// </summary>
public sealed class CamaraComercioVigenciaTests
{
    private static readonly DateOnly Hoy = new(2026, 9, 22);

    // ── AC1 — certificado reciente ───────────────────────────────────────────

    [Fact]
    public void AC1_CertificadoDeHace10Dias_EstaVigenteYNoAlerta()
    {
        var r = CamaraComercioVigencia.Evaluar(Hoy.AddDays(-10), Hoy);

        r.Estado.Should().Be(CamaraComercioVigenciaEstado.Vigente);
        r.Dias.Should().Be(10);
        r.RequiereAlerta.Should().BeFalse();
    }

    [Fact]
    public void AC1_CertificadoExpedidoHoy_EstaVigente()
    {
        var r = CamaraComercioVigencia.Evaluar(Hoy, Hoy);

        r.Estado.Should().Be(CamaraComercioVigenciaEstado.Vigente);
        r.Dias.Should().Be(0);
    }

    // ── AC2 — más de 30 días ─────────────────────────────────────────────────

    [Fact]
    public void AC2_CertificadoDeHace45Dias_ExcedeYReportaLosDias()
    {
        var r = CamaraComercioVigencia.Evaluar(Hoy.AddDays(-45), Hoy);

        r.Estado.Should().Be(CamaraComercioVigenciaEstado.Excedida);
        r.Dias.Should().Be(45);
        r.RequiereAlerta.Should().BeTrue();
    }

    // ── AC3 — sin fecha legible ──────────────────────────────────────────────

    /// <summary>
    /// Una fecha que el OCR no pudo leer NO es un documento vencido. Inventar la advertencia entrena
    /// al gestor a ignorarlas.
    /// </summary>
    [Fact]
    public void AC3_SinFecha_QuedaIndeterminadaYNoAlerta()
    {
        var r = CamaraComercioVigencia.Evaluar(null, Hoy);

        r.Estado.Should().Be(CamaraComercioVigenciaEstado.Indeterminada);
        r.Dias.Should().BeNull();
        r.RequiereAlerta.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no legible")]
    [InlineData("2026-13-45")]
    [InlineData("22/09/2026")] // formato ambiguo: se rechaza en vez de interpretarse
    public void AC3_TextoNoLegible_QuedaIndeterminado(string? texto)
    {
        var r = CamaraComercioVigencia.EvaluarTexto(texto, Hoy);

        r.Estado.Should().Be(CamaraComercioVigenciaEstado.Indeterminada);
        r.RequiereAlerta.Should().BeFalse();
    }

    /// <summary>
    /// <c>dd/MM/yyyy</c> y <c>MM/dd/yyyy</c> son indistinguibles hasta el día 13. Entre alertar de
    /// menos y alertar sobre una fecha mal interpretada, lo segundo es peor.
    /// </summary>
    [Fact]
    public void AC3_NoSeInterpretanFormatosAmbiguos() =>
        CamaraComercioVigencia.ParseFecha("05/06/2026").Should().BeNull();

    [Fact]
    public void ElFormatoDelPromptSiSeLee() =>
        CamaraComercioVigencia.ParseFecha("2026-09-01").Should().Be(new DateOnly(2026, 9, 1));

    // ── AC4 — el corte es estricto ───────────────────────────────────────────

    [Fact]
    public void AC4_ALos30DiasExactos_TodaviaEstaVigente()
    {
        var r = CamaraComercioVigencia.Evaluar(Hoy.AddDays(-30), Hoy);

        r.Estado.Should().Be(CamaraComercioVigenciaEstado.Vigente,
            "a los 30 días exactos el certificado todavía sirve; contarlo como excedido alertaría "
            + "sobre documentos que el organismo aún acepta");
        r.Dias.Should().Be(30);
    }

    [Fact]
    public void AC4_ALos31Dias_YaExcede()
    {
        var r = CamaraComercioVigencia.Evaluar(Hoy.AddDays(-31), Hoy);

        r.Estado.Should().Be(CamaraComercioVigenciaEstado.Excedida);
        r.Dias.Should().Be(31);
    }

    // ── Bordes ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Una fecha futura es un error de lectura, no un certificado del mañana: se cuenta como cero en
    /// vez de devolver un negativo que el cliente tendría que interpretar.
    /// </summary>
    [Fact]
    public void FechaFutura_CuentaComoCeroDiasYNoAlerta()
    {
        var r = CamaraComercioVigencia.Evaluar(Hoy.AddDays(15), Hoy);

        r.Estado.Should().Be(CamaraComercioVigenciaEstado.Vigente);
        r.Dias.Should().Be(0);
        r.RequiereAlerta.Should().BeFalse();
    }

    [Fact]
    public void ElPlazoDeclaradoEsElQuePideElOrganismo() =>
        CamaraComercioVigencia.DiasMaximos.Should().Be(30);

    /// <summary>El contrato con el cliente se escribe a mano: renombrar el enum no puede cambiarlo.</summary>
    [Theory]
    [InlineData(CamaraComercioVigenciaEstado.Vigente, "vigente")]
    [InlineData(CamaraComercioVigenciaEstado.Excedida, "excedida")]
    [InlineData(CamaraComercioVigenciaEstado.Indeterminada, "indeterminada")]
    public void ElContratoDeLaVigenciaEsTextoEstable(CamaraComercioVigenciaEstado estado, string esperado) =>
        CamaraComercioVigencia.ToWire(estado).Should().Be(esperado);
}

using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Tramites.Services;

/// <summary>
/// HU #11136 (Feature #11131) — la tabla certificadora de la RTM aplica solo en traspaso y solo a
/// vehículos con más de 5 años de matriculados.
/// </summary>
public sealed class RtmCertificadoTests
{
    private static readonly DateTimeOffset Hoy = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MatriculaInicial_NuncaLlevaTablaDeRtm()
    {
        RtmCertificado.Aplica(esTraspaso: false, "01/01/2000", Hoy).Should().BeFalse();
    }

    [Fact]
    public void Traspaso_ConVehiculoDeMasDeCincoAnios_LlevaTabla()
    {
        RtmCertificado.Aplica(esTraspaso: true, "15/03/2015", Hoy).Should().BeTrue();
    }

    [Fact]
    public void Traspaso_ConVehiculoNuevo_NoLlevaTabla()
    {
        RtmCertificado.Aplica(esTraspaso: true, "15/03/2025", Hoy).Should().BeFalse();
    }

    // ── La frontera exacta de los 5 años, por ambos lados ────────────────────

    [Fact]
    public void Traspaso_JustoAlCumplirCincoAnios_TodaviaNoLlevaTabla()
    {
        // "más de 5 años": el día en que los cumple aún no los ha superado.
        RtmCertificado.Aplica(esTraspaso: true, "30/07/2021", Hoy).Should().BeFalse();
    }

    [Fact]
    public void Traspaso_UnDiaDespuesDeCumplirCincoAnios_LlevaTabla()
    {
        RtmCertificado.Aplica(esTraspaso: true, "29/07/2021", Hoy).Should().BeTrue();
    }

    // ── Fallo seguro ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es una fecha")]
    [InlineData("Invalid date")]
    public void Traspaso_SinFechaDeMatriculaLegible_LlevaTabla(string? fecha)
    {
        // Omitir una RTM exigible deja el expediente incompleto ante el organismo; incluir una de más
        // solo añade información. Además hay proveedores de RUNT que no reportan esta fecha.
        RtmCertificado.Aplica(esTraspaso: true, fecha, Hoy).Should().BeTrue();
    }

    // ── Formatos que entregan los proveedores ────────────────────────────────

    [Theory]
    [InlineData("15/03/2015")]
    [InlineData("2015-03-15")]
    [InlineData("2015/03/15")]
    [InlineData("15-03-2015")]
    [InlineData("2015-03-15T00:00:00")]
    [InlineData("2015-03-15T00:00:00.000-05:00")]
    public void Interpretar_AceptaLosFormatosDeLosProveedores(string fecha)
    {
        RtmCertificado.Interpretar(fecha).Should().NotBeNull();
        RtmCertificado.Aplica(esTraspaso: true, fecha, Hoy).Should().BeTrue();
    }

    [Fact]
    public void Interpretar_NoConfundeDiaConMes()
    {
        // Los proveedores colombianos escriben el día primero: 03/12/2015 es 3 de diciembre.
        var fecha = RtmCertificado.Interpretar("03/12/2015");

        fecha.Should().NotBeNull();
        fecha!.Value.Month.Should().Be(12);
        fecha.Value.Day.Should().Be(3);
    }
}

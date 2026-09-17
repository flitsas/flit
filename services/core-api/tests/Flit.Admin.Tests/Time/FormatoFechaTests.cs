using Flit.Queries.Domain.Time;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Time;

/// <summary>
/// HU #12665 — formato único de presentación del backend.
///
/// <para>
/// Los valores esperados van LITERALES y no compuestos con las constantes de
/// <see cref="FormatoFecha"/>: si el test tomara el patrón de la misma clase que prueba, cambiarlo
/// por error pasaría inadvertido.
/// </para>
/// </summary>
public class FormatoFechaTests
{
    [Fact]
    public void Instante_MuestraDiaMesAnioYHoraEnColombia()
    {
        // 19:05 UTC son las 14:05 en Colombia.
        var instante = new DateTimeOffset(2026, 9, 17, 19, 5, 0, TimeSpan.Zero);

        FormatoFecha.Instante(instante).Should().Be("17/09/2026 14:05");
    }

    [Fact]
    public void Instante_NoMuestraSegundos()
    {
        var instante = new DateTimeOffset(2026, 9, 17, 19, 5, 42, TimeSpan.Zero);

        FormatoFecha.Instante(instante).Should().Be("17/09/2026 14:05");
    }

    [Fact]
    public void Instante_NoDependeDelHusoEnQueVengaExpresado()
    {
        var mismoInstante = new[]
        {
            new DateTimeOffset(2026, 9, 17, 19, 5, 0, TimeSpan.Zero),           // UTC
            new DateTimeOffset(2026, 9, 17, 21, 5, 0, TimeSpan.FromHours(2)),   // Madrid
            new DateTimeOffset(2026, 9, 17, 14, 5, 0, TimeSpan.FromHours(-5)),  // ya en Colombia
        };

        mismoInstante.Select(FormatoFecha.Instante)
            .Should().OnlyContain(s => s == "17/09/2026 14:05");
    }

    [Theory]
    // Medianoche de Colombia es 05:00 UTC: ahí cambia el día que se muestra.
    [InlineData("2026-09-17T04:59:00Z", "16/09/2026 23:59")]
    [InlineData("2026-09-17T05:00:00Z", "17/09/2026 00:00")]
    [InlineData("2026-09-18T02:30:00Z", "17/09/2026 21:30")]
    public void Instante_CruzaBienLaMedianoche(string utc, string esperado)
    {
        var instante = DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);

        FormatoFecha.Instante(instante).Should().Be(esperado);
    }

    [Fact]
    public void Instante_SinValorDevuelveCadenaVacia()
    {
        // Vacío y no un guión: en una celda de Excel o de un PDF un guión se lee como dato.
        FormatoFecha.Instante((DateTimeOffset?)null).Should().BeEmpty();
        FormatoFecha.Instante(null, "sin fecha").Should().Be("sin fecha");
    }

    [Fact]
    public void Calendario_NoLlevaHoraNiSeConvierteDeZona()
    {
        // RN-08: una fecha de calendario no tiene hora, y convertirla correría el día.
        FormatoFecha.Calendario(new DateOnly(2026, 7, 1)).Should().Be("01/07/2026");
        FormatoFecha.Calendario(new DateOnly(2026, 12, 31)).Should().Be("31/12/2026");
    }

    [Fact]
    public void Calendario_SinValorDevuelveCadenaVacia()
    {
        FormatoFecha.Calendario((DateOnly?)null).Should().BeEmpty();
        FormatoFecha.Calendario(null, "—").Should().Be("—");
    }

    [Fact]
    public void LosDosPatronesSonNumericosYNoDependenDeLaCultura()
    {
        // Importa porque los proyectos compilan con InvariantGlobalization: si el patrón usara el
        // nombre del mes, el resultado cambiaría (o se degradaría) sin aviso.
        FormatoFecha.PatronInstante.Should().Be("dd/MM/yyyy HH:mm");
        FormatoFecha.PatronCalendario.Should().Be("dd/MM/yyyy");
        FormatoFecha.PatronInstante.Should().NotContain("MMM");
    }
}

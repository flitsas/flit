using System.Reflection;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>HU13154c — la fecha sola del trámite es el mismo día en el mandato y en el FUR.</summary>
public sealed class FechaTramiteSinDesplazamientoTests
{
    [Theory]
    [InlineData("2026-10-01", 2026, 10, 1)]
    [InlineData(" 2026-10-01 ", 2026, 10, 1)]
    [InlineData("01/10/2026", 2026, 10, 1)]
    [InlineData("2026-01-01", 2026, 1, 1)]
    public void FechaSola_ConservaElDiaCalendario(string raw, int y, int m, int d)
    {
        var fecha = FechaTramiteParser.Parse(raw);

        fecha.Should().NotBeNull();
        (fecha!.Value.Year, fecha.Value.Month, fecha.Value.Day).Should().Be((y, m, d));
    }

    [Fact]
    public void FechaConHoraExplicita_SigueComoAntes()
    {
        var esperado = DateTime.Parse(
            "2026-10-01T03:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal);

        FechaTramiteParser.Parse("2026-10-01T03:00:00Z").Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es fecha")]
    public void SinFechaValida_EsNula(string? raw) => FechaTramiteParser.Parse(raw).Should().BeNull();

    [Fact]
    public void Mandato_FormateaElMismoDiaQueElFur()
    {
        var fecha = FechaTramiteParser.Parse("2026-10-01")!.Value;

        var formatear = typeof(MandatoPdfGenerator)
            .GetMethod("FormatFechaEs", BindingFlags.NonPublic | BindingFlags.Static)!;
        formatear.Invoke(null, [fecha]).Should().Be("1 de octubre de 2026");
        (fecha.Day, fecha.Month, fecha.Year).Should().Be((1, 10, 2026));
    }
}

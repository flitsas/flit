using Flit.Comparendos.Domain.Calendario;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Dominio;

public sealed class ExpresionCronTests
{
    [Theory]
    [InlineData("0 7,13 * * *", "2026-10-09 07:00", true)]
    [InlineData("0 7,13 * * *", "2026-10-09 13:00", true)]
    [InlineData("0 7,13 * * *", "2026-10-09 07:01", false)]
    [InlineData("*/15 * * * *", "2026-10-09 10:45", true)]
    [InlineData("*/15 * * * *", "2026-10-09 10:46", false)]
    [InlineData("0 17 * * 5", "2026-10-09 17:00", true)]
    [InlineData("0 17 * * 5", "2026-10-08 17:00", false)]
    [InlineData("0 18 L * *", "2026-10-31 18:00", true)]
    [InlineData("0 18 L * *", "2026-10-30 18:00", false)]
    [InlineData("0 18 L * *", "2028-02-29 18:00", true)]
    [InlineData("30 8-18/2 * * 1-5", "2026-10-09 10:30", true)]
    [InlineData("30 8-18/2 * * 1-5", "2026-10-10 10:30", false)]
    public void Coincide(string cron, string momento, bool esperado) =>
        ExpresionCron.Parse(cron).Coincide(DateTime.Parse(momento, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(esperado);

    [Fact]
    public void Siguiente_salta_al_proximo_dia() =>
        ExpresionCron.Parse("0 7,13 * * *").Siguiente(new DateTime(2026, 10, 9, 13, 0, 0)).Should().Be(new DateTime(2026, 10, 10, 7, 0, 0));

    [Theory]
    [InlineData("0 7 * *")]
    [InlineData("61 * * * *")]
    [InlineData("0 25 * * *")]
    [InlineData("a b c d e")]
    public void Expresiones_invalidas(string cron) => ExpresionCron.EsValida(cron, out _).Should().BeFalse();

    /// <summary>Hallazgo B4-2: la tabla de trabajos dice la programación en palabras.</summary>
    [Theory]
    [InlineData("*/15 * * * *", "Cada 15 minutos")]
    [InlineData("0 7,13 * * *", "Todos los días a las 7:00 y 13:00")]
    [InlineData("0 1 * * *", "Todos los días a la 1:00")]
    [InlineData("0 17 * * 5", "Los viernes a las 17:00")]
    [InlineData("30 6 * * 1,3", "Los lunes y miércoles a las 6:30")]
    [InlineData("0 6 * * 1-5", "De lunes a viernes a las 6:00")]
    [InlineData("0 18 L * *", "El último día de cada mes a las 18:00")]
    [InlineData("0 8 15 * *", "El día 15 de cada mes a las 8:00")]
    [InlineData("0 8 * 1 *", null)]
    public void Describir_EnPalabras(string cron, string? esperado) => ExpresionCron.Describir(cron).Should().Be(esperado);
}

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
}

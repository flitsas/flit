using Flit.Queries.Domain.Time;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Time;

/// <summary>
/// HU #12662 — el huso de negocio es único y se resuelve con offset fijo.
///
/// <para>
/// Los valores esperados van LITERALES (−5 horas escritas a mano, días escritos a mano) y no
/// calculados con <see cref="ColombiaTime"/>: si el test tomara el desfase de la misma clase que
/// prueba, un cambio erróneo del offset pasaría inadvertido.
/// </para>
/// </summary>
public class ColombiaTimeTests
{
    /// <summary>Reloj detenido, para no depender de la hora real al probar <c>Today</c>.</summary>
    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    [Fact]
    public void Offset_EsCincoHorasDetrasDeUtc()
    {
        ColombiaTime.Offset.Should().Be(TimeSpan.FromHours(-5));
    }

    [Fact]
    public void From_ConservaElInstanteYSoloCambiaLaRepresentacion()
    {
        var instante = new DateTimeOffset(2026, 9, 17, 14, 30, 7, TimeSpan.Zero);

        var enColombia = ColombiaTime.From(instante);

        enColombia.UtcDateTime.Should().Be(instante.UtcDateTime, "convertir no mueve el instante");
        enColombia.Offset.Should().Be(TimeSpan.FromHours(-5));
        enColombia.Hour.Should().Be(9);
        enColombia.Minute.Should().Be(30);
        enColombia.Second.Should().Be(7);
    }

    [Fact]
    public void From_NoDependeDelHusoEnQueVengaExpresadoElInstante()
    {
        var mismoInstante = new[]
        {
            new DateTimeOffset(2026, 9, 17, 14, 30, 0, TimeSpan.Zero),          // UTC
            new DateTimeOffset(2026, 9, 17, 16, 30, 0, TimeSpan.FromHours(2)),  // Madrid
            new DateTimeOffset(2026, 9, 17, 23, 30, 0, TimeSpan.FromHours(9)),  // Tokio
        };

        mismoInstante.Select(ColombiaTime.From)
            .Should().OnlyContain(d => d.Hour == 9 && d.Minute == 30,
                "el mismo instante es la misma hora de Colombia, venga como venga");
    }

    [Theory]
    // Medianoche de Colombia es 05:00 UTC: ahí empieza el día calendario.
    [InlineData("2026-09-17T04:59:59Z", 2026, 9, 16)]
    [InlineData("2026-09-17T05:00:00Z", 2026, 9, 17)]
    [InlineData("2026-09-17T23:59:59Z", 2026, 9, 17)]
    [InlineData("2026-09-18T02:00:00Z", 2026, 9, 17)]  // aún es "ayer" en Colombia
    public void DayOf_DevuelveElDiaCalendarioDeColombia(string utc, int año, int mes, int dia)
    {
        var instante = DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);

        ColombiaTime.DayOf(instante).Should().Be(new DateOnly(año, mes, dia));
    }

    [Fact]
    public void Today_SaleDelRelojRecibidoYNoDelSistema()
    {
        // 01:30 UTC del día 18 todavía es el día 17 en Colombia.
        var reloj = new RelojFijo(new DateTimeOffset(2026, 9, 18, 1, 30, 0, TimeSpan.Zero));

        ColombiaTime.Today(reloj).Should().Be(new DateOnly(2026, 9, 17));
    }

    [Fact]
    public void Today_RechazaUnRelojNulo()
    {
        var acto = () => ColombiaTime.Today(null!);

        acto.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Zone_EsElMismoHusoQueElOffsetYNoTieneHorarioDeVerano()
    {
        ColombiaTime.Zone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(-5));
        ColombiaTime.Zone.SupportsDaylightSavingTime.Should().BeFalse(
            "Colombia no aplica horario de verano desde 1993");
        ColombiaTime.Zone.GetAdjustmentRules().Should().BeEmpty();
    }

    [Fact]
    public void Zone_ConvierteIgualQueFrom()
    {
        // Las dos vías coexisten porque algunas APIs exigen TimeZoneInfo; deben coincidir siempre.
        var instante = new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero);

        TimeZoneInfo.ConvertTime(instante, ColombiaTime.Zone)
            .Should().Be(ColombiaTime.From(instante));
    }

    [Fact]
    public void Zone_SeConstruyeSinConsultarLaBaseDeHusosDelSistema()
    {
        // No se comprueba que FindSystemTimeZoneById falle: en Linux con ICU sí resuelve, y el
        // test correría distinto según la máquina. Lo que se fija es que ColombiaTime NO dependa
        // de esa búsqueda — el guardián de que nadie la reintroduzca está en
        // ColombiaTimeArchitectureTests.
        var acto = () => ColombiaTime.Zone.GetUtcOffset(DateTimeOffset.UtcNow);

        acto.Should().NotThrow<TimeZoneNotFoundException>();
    }

    [Fact]
    public void UnaFechaDeCalendarioNoDebePasarPorAqui_RegresionHu11194()
    {
        // RN-08. Documenta POR QUÉ el helper no ofrece una conversión para DateOnly: una vigencia
        // "2026-07-01" leída como instante UTC y convertida a Colombia retrocede al día anterior,
        // que es exactamente el defecto que corrigió la HU #11194.
        var vigenciaComoInstanteUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        ColombiaTime.DayOf(vigenciaComoInstanteUtc)
            .Should().Be(new DateOnly(2026, 6, 30), "por esto una fecha de calendario no se convierte");
    }
}

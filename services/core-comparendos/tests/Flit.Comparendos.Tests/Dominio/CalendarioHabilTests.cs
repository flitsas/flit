using Flit.Comparendos.Domain.Calendario;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Dominio;

public sealed class CalendarioHabilTests
{
    private static readonly CalendarioHabil Calendario = CalendarioHabil.Calculado(2024, 2027);

    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    public void DomingoDePascua_coincide_con_el_calendario_oficial(int anio, int mes, int dia) =>
        FestivosColombia.DomingoDePascua(anio).Should().Be(new DateOnly(anio, mes, dia));

    private static readonly string[] Festivos2026 =
    [
            "2026-01-01", "2026-01-12", "2026-03-23", "2026-04-02", "2026-04-03", "2026-05-01", "2026-05-18",
            "2026-06-08", "2026-06-15", "2026-06-29", "2026-07-20", "2026-08-07", "2026-08-17", "2026-10-12",
            "2026-11-02", "2026-11-16", "2026-12-08", "2026-12-25",
    ];

    [Fact]
    public void Festivos_2026_son_los_del_decreto()
    {
        var esperados = Festivos2026.Select(f => DateOnly.Parse(f, System.Globalization.CultureInfo.InvariantCulture));

        FestivosColombia.DelAnio(2026).Select(f => f.Fecha).Should().Equal(esperados);
    }

    [Fact]
    public void Festivos_que_coinciden_el_mismo_lunes_son_un_solo_dia()
    {
        var julio2019 = FestivosColombia.DelAnio(2019).Where(f => f.Fecha == new DateOnly(2019, 7, 1)).ToList();
        julio2019.Should().ContainSingle().Which.Nombre.Should().Be("Sagrado Corazón de Jesús y San Pedro y San Pablo");
    }

    [Fact]
    public void SumarHabiles_salta_fin_de_semana_y_festivo()
    {
        // Viernes 9-oct-2026; el lunes 12 es Día de la Raza: el hábil 1 es el martes 13.
        Calendario.SumarHabiles(new DateOnly(2026, 10, 9), 1).Should().Be(new DateOnly(2026, 10, 13));
        Calendario.SumarHabiles(new DateOnly(2026, 10, 9), 11).Should().Be(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public void SumarHabiles_con_cero_devuelve_la_misma_fecha() =>
        Calendario.SumarHabiles(new DateOnly(2026, 10, 10), 0).Should().Be(new DateOnly(2026, 10, 10));

    [Fact]
    public void HabilesTranscurridos_es_inverso_de_SumarHabiles()
    {
        var desde = new DateOnly(2026, 3, 20);
        for (var n = 0; n <= 30; n++)
            Calendario.HabilesTranscurridos(desde, Calendario.SumarHabiles(desde, n)).Should().Be(n);
    }

    [Fact]
    public void HabilesRestantes_es_cero_si_el_limite_ya_paso() =>
        Calendario.HabilesRestantes(new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 4)).Should().Be(0);
}

using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Estimacion;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13747 (F01-H3, R-11) — el estimado de consultas por fuente es «vehículos válidos menos N/A previsibles», en número de
/// consultas y nunca en pesos. En D1 los N/A previsibles son RUNT_LOCATARIO y SIMIT_LOCATARIO sin documento de locatario.
/// </summary>
/// <example>
/// <code>
/// EstimadorConsultas.Estimar([Fuente.Runt], Enumerable.Repeat(new VehiculoAEstimar(true, false), 10))
/// // → [EstimadoPorFuente(Fuente.Runt, 10)]
/// </code>
/// </example>
public sealed class EstimadorConsultasTests
{
    private static IEnumerable<VehiculoAEstimar> Lote(int conLocatario, int sinLocatario, int invalidos = 0) =>
        Enumerable.Repeat(new VehiculoAEstimar(true, true), conLocatario)
            .Concat(Enumerable.Repeat(new VehiculoAEstimar(true, false), sinLocatario))
            .Concat(Enumerable.Repeat(new VehiculoAEstimar(false, true), invalidos));

    // Escenario 1: 10 válidos con alcance RUNT, SIMIT_VEHICULO → 10 consultas por fuente.
    [Fact]
    public void Diez_validos_por_RUNT_y_SIMIT_VEHICULO_dan_10_consultas_por_fuente()
    {
        var estimado = EstimadorConsultas.Estimar([Fuente.Runt, Fuente.SimitVehiculo], Lote(conLocatario: 3, sinLocatario: 7));

        estimado.Should().Equal(new EstimadoPorFuente(Fuente.Runt, 10), new EstimadoPorFuente(Fuente.SimitVehiculo, 10));
    }

    // Escenario 2: 10 válidos, 4 sin documento de locatario → RUNT_LOCATARIO suma 6.
    [Theory]
    [InlineData(Fuente.RuntLocatario)]
    [InlineData(Fuente.SimitLocatario)]
    public void Diez_validos_con_4_sin_locatario_restan_los_NA_previsibles_de_las_fuentes_de_locatario(Fuente fuente)
    {
        var estimado = EstimadorConsultas.Estimar([fuente, Fuente.Runt], Lote(conLocatario: 6, sinLocatario: 4));

        estimado.Should().Equal(new EstimadoPorFuente(fuente, 6), new EstimadoPorFuente(Fuente.Runt, 10));
    }

    // Las fuentes que no son de locatario no restan N/A en D1.
    [Theory]
    [InlineData(Fuente.Runt)]
    [InlineData(Fuente.RuntPropietario)]
    [InlineData(Fuente.SimitVehiculo)]
    [InlineData(Fuente.SimitPropietario)]
    [InlineData(Fuente.Avaluo)]
    [InlineData(Fuente.ImpuestoDepartamental)]
    public void Las_fuentes_que_no_son_de_locatario_cuentan_todos_los_validos(Fuente fuente)
    {
        EstimadorConsultas.Estimar([fuente], Lote(conLocatario: 6, sinLocatario: 4))
            .Should().Equal(new EstimadoPorFuente(fuente, 10));
    }

    [Fact]
    public void Los_vehiculos_invalidos_no_cuentan_en_ninguna_fuente()
    {
        EstimadorConsultas.Estimar([Fuente.Runt, Fuente.RuntLocatario], Lote(conLocatario: 2, sinLocatario: 1, invalidos: 5))
            .Should().Equal(new EstimadoPorFuente(Fuente.Runt, 3), new EstimadoPorFuente(Fuente.RuntLocatario, 2));
    }

    [Fact]
    public void Un_lote_vacio_da_0_consultas_por_fuente()
    {
        EstimadorConsultas.Estimar([Fuente.Runt, Fuente.SimitLocatario], [])
            .Should().Equal(new EstimadoPorFuente(Fuente.Runt, 0), new EstimadoPorFuente(Fuente.SimitLocatario, 0));
    }

    [Fact]
    public void Una_fuente_repetida_en_el_alcance_se_estima_una_sola_vez()
    {
        EstimadorConsultas.Estimar([Fuente.Runt, Fuente.Runt], Lote(conLocatario: 1, sinLocatario: 1))
            .Should().Equal(new EstimadoPorFuente(Fuente.Runt, 2));
    }

    [Fact]
    public void Sin_alcance_no_hay_estimado()
    {
        EstimadorConsultas.Estimar([], Lote(conLocatario: 1, sinLocatario: 1)).Should().BeEmpty();
    }

    // Contrato: el resultado no expone montos, solo la fuente y el número de consultas.
    [Fact]
    public void El_resultado_no_expone_montos_solo_fuente_y_numero_de_consultas()
    {
        var propiedades = typeof(EstimadoPorFuente).GetProperties().ToDictionary(p => p.Name, p => p.PropertyType);

        propiedades.Should().BeEquivalentTo(new Dictionary<string, Type>
        {
            [nameof(EstimadoPorFuente.Fuente)] = typeof(Fuente),
            [nameof(EstimadoPorFuente.Consultas)] = typeof(int),
        });
    }

    [Fact]
    public void Rechaza_argumentos_nulos()
    {
        var sinAlcance = () => EstimadorConsultas.Estimar(null!, []);
        var sinVehiculos = () => EstimadorConsultas.Estimar([Fuente.Runt], null!);

        sinAlcance.Should().Throw<ArgumentNullException>();
        sinVehiculos.Should().Throw<ArgumentNullException>();
    }
}

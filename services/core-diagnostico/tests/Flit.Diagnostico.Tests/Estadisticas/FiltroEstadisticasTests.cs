using Flit.Diagnostico.Api.Modulos.Estadisticas;
using Flit.Diagnostico.Api.Plataforma;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Flit.Diagnostico.Tests.Estadisticas;

/// <summary>
/// HU #14579, BE-1 (contrato-api §0 y §1; SP-9, SP-11): filtro de las estadísticas E1–E3. <c>desde</c> y <c>hasta</c> obligatorios
/// (AAAA-MM-DD, hora Colombia, días completos), <c>hasta ≤ hoy</c> según <see cref="TimeProvider"/> y como máximo 366 días contados
/// con los dos extremos incluidos (el mismo criterio del front). <c>empresa</c> fuera de E3, <c>documento</c> y <c>nombre</c>: 400
/// <c>FILTRO_INVALIDO</c>. Sin base.
/// </summary>
/// <example>
/// <code>
/// var filtro = FiltroEstadisticas.Leer(new QueryCollection(...), reloj, admiteEmpresa: false);
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~FiltroEstadisticasTests
/// </code>
/// </example>
public sealed class FiltroEstadisticasTests
{
    // 2026-11-15 10:00 hora Colombia (15:00 UTC).
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 11, 15, 15, 0, 0, TimeSpan.Zero));

    // AC «rango y parámetros inválidos» — rango válido: días completos en hora Colombia (UTC−5), hasta exclusivo al día siguiente.
    [Fact]
    public void BE1_rango_valido_da_dias_completos_en_hora_Colombia()
    {
        var filtro = FiltroEstadisticas.Leer(Consulta(("desde", "2026-11-01"), ("hasta", "2026-11-15")), Reloj, admiteEmpresa: false);

        filtro.Desde.Should().Be(new DateOnly(2026, 11, 1));
        filtro.Hasta.Should().Be(new DateOnly(2026, 11, 15));
        filtro.Dias.Should().Be(15, "los dos extremos cuentan");
        filtro.Inicio.Should().Be(new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero));
        filtro.Fin.Should().Be(new DateTimeOffset(2026, 11, 16, 5, 0, 0, TimeSpan.Zero));
    }

    // 366 días con los dos extremos incluidos pasan; 367 no (el front cuenta igual).
    [Fact]
    public void BE1_366_dias_incluidos_pasan_y_367_dan_RANGO_INVALIDO()
    {
        var justo = FiltroEstadisticas.Leer(Consulta(("desde", "2025-11-15"), ("hasta", "2026-11-15")), Reloj, admiteEmpresa: false);
        justo.Dias.Should().Be(366);

        var largo = () => FiltroEstadisticas.Leer(Consulta(("desde", "2025-11-14"), ("hasta", "2026-11-15")), Reloj, admiteEmpresa: false);

        largo.Should().Throw<ReglaException>().Which.Codigo.Should().Be("RANGO_INVALIDO");
    }

    [Theory]
    [InlineData("2026-11-10", "2026-11-01", "RANGO_INVALIDO", "hasta")]
    [InlineData("2026-11-01", "2026-11-16", "RANGO_INVALIDO", "hasta")]
    [InlineData(null, "2026-11-15", "FILTRO_INVALIDO", "desde")]
    [InlineData("2026-11-01", null, "FILTRO_INVALIDO", "hasta")]
    [InlineData("15/11/2026", "2026-11-15", "FILTRO_INVALIDO", "desde")]
    [InlineData("2026-11-01", "mañana", "FILTRO_INVALIDO", "hasta")]
    public void BE1_fechas_ausentes_mal_formadas_invertidas_o_futuras_dan_400(string? desde, string? hasta, string codigo, string campo)
    {
        var pares = new List<(string, string)>();
        if (desde is not null)
            pares.Add(("desde", desde));
        if (hasta is not null)
            pares.Add(("hasta", hasta));

        var leer = () => FiltroEstadisticas.Leer(Consulta([.. pares]), Reloj, admiteEmpresa: false);

        var error = leer.Should().Throw<ReglaException>().Which;
        error.Codigo.Should().Be(codigo);
        error.Campo.Should().Be(campo);
        error.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    // «hoy» es el día de Colombia: a las 23:30 del 15 en Colombia (04:30 UTC del 16) el 16 sigue siendo futuro.
    [Fact]
    public void BE1_hoy_es_el_dia_de_Colombia_no_el_UTC()
    {
        var noche = new RelojFijo(new DateTimeOffset(2026, 11, 16, 4, 30, 0, TimeSpan.Zero));

        var hoy = FiltroEstadisticas.Leer(Consulta(("desde", "2026-11-15"), ("hasta", "2026-11-15")), noche, admiteEmpresa: false);
        var manana = () => FiltroEstadisticas.Leer(Consulta(("desde", "2026-11-15"), ("hasta", "2026-11-16")), noche, admiteEmpresa: false);

        hoy.Dias.Should().Be(1);
        manana.Should().Throw<ReglaException>().Which.Codigo.Should().Be("RANGO_INVALIDO");
    }

    // SP-9 y contrato §0: documento y nombre nunca; empresa solo en E3; un parámetro único repetido; los desconocidos se ignoran.
    [Theory]
    [InlineData("documento", "1", false)]
    [InlineData("nombre", "x", false)]
    [InlineData("documento", "1", true)]
    [InlineData("empresa", "0192f1a2-0000-7000-8000-000000000001", false)]
    [InlineData("desde", "2026-11-02", false)]
    public void BE1_documento_nombre_empresa_fuera_de_E3_o_repetidos_dan_FILTRO_INVALIDO(string clave, string valor, bool admiteEmpresa)
    {
        var leer = () => FiltroEstadisticas.Leer(Consulta(("desde", "2026-11-01"), ("hasta", "2026-11-15"), (clave, valor)), Reloj, admiteEmpresa);

        var error = leer.Should().Throw<ReglaException>().Which;
        error.Codigo.Should().Be("FILTRO_INVALIDO");
        error.Campo.Should().Be(clave);
        error.Message.Should().NotContain(valor, "SP-9: el mensaje no repite el valor de la consulta");
    }

    [Fact]
    public void BE1_E3_admite_empresa_y_los_desconocidos_se_ignoran()
    {
        var filtro = FiltroEstadisticas.Leer(
            Consulta(("desde", "2026-11-01"), ("hasta", "2026-11-15"), ("empresa", Guid.NewGuid().ToString()), ("otro", "x")), Reloj, admiteEmpresa: true);

        filtro.Dias.Should().Be(15);
    }

    private static QueryCollection Consulta(params (string Clave, string Valor)[] pares) =>
        new(pares.GroupBy(p => p.Clave).ToDictionary(g => g.Key, g => new StringValues([.. g.Select(p => p.Valor)])));

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}

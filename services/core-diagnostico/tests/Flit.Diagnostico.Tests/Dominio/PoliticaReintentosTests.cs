using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13737 (F03-H6), D-12; ajuste 2026-11-11 Punto 3; HU #14752 (D5 = a) — política de reintentos: un transitorio con
/// <c>intentos_auto</c> menor que <c>MaxReintentos</c> (por defecto 3) vuelve a la cola con
/// <c>disponible_desde = ahora + base × 2^(n−1) × (1 ± factor)</c>: 1, 2 y 4 min ±20 % con los valores por defecto, y al menos
/// 60 s si el error es <c>LIMITE_PROVEEDOR</c>. Un definitivo o los reintentos agotados no se reintentan. El instante sale de
/// un reloj inyectable (<see cref="RelojManual"/>) y el azar lo da quien llama, así que no hay esperas ni aleatoriedad en la
/// prueba. <c>MaxReintentos</c> ∈ [0, 3]; la espera base ∈ [1 s, 1 h]; el factor ∈ [0, 0,5].
/// </summary>
/// <example>
/// <code>
/// new PoliticaReintentos(3, TimeSpan.FromMinutes(1), 0.2, TimeSpan.FromSeconds(60)).Decidir("TIMEOUT", 0, ahora, aleatorio: 0.5); // ahora + 1 min
/// </code>
/// </example>
public sealed class PoliticaReintentosTests
{
    private static PoliticaReintentos PorDefecto() => new(
        PoliticaReintentos.PorDefecto, PoliticaReintentos.EsperaPorDefecto, PoliticaReintentos.FactorAleatorioPorDefecto,
        PoliticaReintentos.EsperaMinimaLimitePorDefecto);

    // HU #14752 — por defecto: 3 reintentos, base de 1 min, ±20 % y mínimo de 60 s para el límite del proveedor.
    [Fact]
    public void Por_defecto_son_3_reintentos_con_base_de_1_minuto_20_por_ciento_y_60_segundos_de_limite()
    {
        PoliticaReintentos.PorDefecto.Should().Be(3);
        PoliticaReintentos.EsperaPorDefecto.Should().Be(TimeSpan.FromMinutes(1));
        PoliticaReintentos.FactorAleatorioPorDefecto.Should().Be(0.2);
        PoliticaReintentos.FactorAleatorioMaximo.Should().Be(0.5);
        PoliticaReintentos.EsperaMinimaLimitePorDefecto.Should().Be(TimeSpan.FromSeconds(60));
        PoliticaReintentos.CodigoLimite.Should().Be("LIMITE_PROVEEDOR");
        new PoliticaReintentos(PoliticaReintentos.PorDefecto).Espera.Should().Be(TimeSpan.FromMinutes(1), "sin espera explícita, la base por defecto");
    }

    // HU #14752 — con factor 0 el backoff es 1, 2 y 4 min; el cuarto queda en ERROR.
    [Fact]
    public void Con_factor_0_el_backoff_es_1_2_y_4_minutos_y_luego_queda_en_ERROR()
    {
        var reloj = new RelojManual();
        var ahora = reloj.GetUtcNow();
        var tres = new PoliticaReintentos(3, TimeSpan.FromMinutes(1), 0, PoliticaReintentos.EsperaMinimaLimitePorDefecto);

        tres.Decidir("TIMEOUT", 0, ahora).Should().Be(new DecisionReintento(true, ahora.AddMinutes(1), 1));
        tres.Decidir("RED", 1, ahora).Should().Be(new DecisionReintento(true, ahora.AddMinutes(2), 2), "la espera se duplica");
        tres.Decidir("HTTP_5XX", 2, ahora).Should().Be(new DecisionReintento(true, ahora.AddMinutes(4), 3));
        tres.Decidir("TIMEOUT", 3, ahora).Should().Be(new DecisionReintento(false, null, 3), "agotados los 3 reintentos queda en ERROR");

        new PoliticaReintentos(0).Decidir("TIMEOUT", 0, ahora).Should().Be(new DecisionReintento(false, null, 0), "con 0 no se reintenta");

        reloj.Avanzar(TimeSpan.FromMinutes(1));
        tres.Decidir("TIMEOUT", 0, reloj.GetUtcNow()).DisponibleDesde.Should().Be(ahora.AddMinutes(2), "la espera cuenta desde el reloj dado");
    }

    // HU #14752 — el azar 0 da ×0,8 y el azar 1 da ×1,2 (factor 0,2); 0,5 deja la espera exacta.
    [Theory]
    [InlineData(0, 0.0, 48_000)]
    [InlineData(0, 0.5, 60_000)]
    [InlineData(0, 1.0, 72_000)]
    [InlineData(1, 0.0, 96_000)]
    [InlineData(1, 1.0, 144_000)]
    [InlineData(2, 0.0, 192_000)]
    [InlineData(2, 1.0, 288_000)]
    public void El_azar_mueve_la_espera_20_por_ciento_hacia_cada_lado(int intentosAuto, double aleatorio, int milisegundos)
    {
        var ahora = DateTimeOffset.UnixEpoch;

        var decision = PorDefecto().Decidir("TIMEOUT", intentosAuto, ahora, aleatorio);

        decision.DisponibleDesde.Should().Be(ahora.AddMilliseconds(milisegundos));
    }

    // HU #14752 — DisponibleDesde va redondeado a milisegundos.
    [Fact]
    public void La_espera_se_redondea_a_milisegundos()
    {
        var ahora = DateTimeOffset.UnixEpoch;

        var desde = PorDefecto().Decidir("TIMEOUT", 0, ahora, 0.123456789).DisponibleDesde!.Value;

        ((desde - ahora).Ticks % TimeSpan.TicksPerMillisecond).Should().Be(0);
        (desde - ahora).TotalMilliseconds.Should().Be(Math.Round(60_000 * (1 + (0.2 * ((2 * 0.123456789) - 1)))));
    }

    // HU #14752 — LIMITE_PROVEEDOR espera al menos el mínimo (60 s), aunque la base sea de 1 s.
    [Fact]
    public void LIMITE_PROVEEDOR_con_base_de_1_segundo_espera_60_segundos()
    {
        var ahora = DateTimeOffset.UnixEpoch;
        var politica = new PoliticaReintentos(3, TimeSpan.FromSeconds(1), 0.2, TimeSpan.FromSeconds(60));

        politica.Decidir("LIMITE_PROVEEDOR", 0, ahora, 1.0).Should().Be(new DecisionReintento(true, ahora.AddSeconds(60), 1));
        politica.Decidir("LIMITE_PROVEEDOR", 2, ahora, 0.0).DisponibleDesde.Should().Be(ahora.AddSeconds(60));
        politica.Decidir("TIMEOUT", 0, ahora, 0.5).DisponibleDesde.Should().Be(ahora.AddSeconds(1), "el mínimo solo aplica al límite del proveedor");

        PorDefecto().Decidir("LIMITE_PROVEEDOR", 0, ahora, 0.0).DisponibleDesde.Should().Be(ahora.AddSeconds(60), "48 s sube a 60 s");
        PorDefecto().Decidir("LIMITE_PROVEEDOR", 1, ahora, 0.0).DisponibleDesde.Should().Be(ahora.AddSeconds(96), "por encima del mínimo manda el backoff");
    }

    // HU #14752 — la espera máxima efectiva (la usan el número de colas de espera y el umbral de pegada).
    [Fact]
    public void La_espera_maxima_efectiva_por_defecto_es_4_minutos_y_48_segundos()
    {
        PorDefecto().EsperaMaximaEfectiva.Should().Be(TimeSpan.FromSeconds(288));
        new PoliticaReintentos(0, TimeSpan.FromSeconds(1), 0, TimeSpan.FromSeconds(60)).EsperaMaximaEfectiva
            .Should().Be(TimeSpan.FromSeconds(60), "con 0 reintentos cuenta al menos uno, y nunca menos que el mínimo del límite");
        new PoliticaReintentos(1, TimeSpan.FromMinutes(5), 0.5, TimeSpan.FromSeconds(60)).EsperaMaximaEfectiva.Should().Be(TimeSpan.FromMinutes(7.5));
    }

    // HU #14752 — los constructores viejos siguen sin azar: el aleatorio no mueve la espera.
    [Fact]
    public void Los_constructores_viejos_siguen_sin_azar()
    {
        var ahora = DateTimeOffset.UnixEpoch;
        var politica = new PoliticaReintentos(2, TimeSpan.FromSeconds(30));

        politica.Espera.Should().Be(TimeSpan.FromSeconds(30));
        politica.Decidir("TIMEOUT", 0, ahora, 0.0).Should().Be(new DecisionReintento(true, ahora.AddSeconds(30), 1));
        politica.Decidir("TIMEOUT", 1, ahora, 1.0).Should().Be(new DecisionReintento(true, ahora.AddSeconds(60), 2));
        politica.Decidir("TIMEOUT", 2, ahora).Reintentar.Should().BeFalse();
        new PoliticaReintentos(3).Decidir("TIMEOUT", 0, ahora, 0.0).DisponibleDesde.Should().Be(ahora.AddMinutes(1));
    }

    // Punto 3 AC2 — un definitivo no se reintenta aunque queden reintentos.
    [Theory]
    [InlineData("RESPUESTA_INVALIDA")]
    [InlineData("NO_CONECTADO")]
    [InlineData(null)]
    public void Definitivo_no_se_reintenta(string? codigo) =>
        new PoliticaReintentos(3).Decidir(codigo, 0, DateTimeOffset.UnixEpoch).Should().Be(new DecisionReintento(false, null, 0));

    // D-12 — MaxReintentos fuera de [0, 3] no es una política válida.
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void MaxReintentos_fuera_de_rango_lanza(int max) =>
        FluentActions.Invoking(() => new PoliticaReintentos(max)).Should().Throw<ArgumentOutOfRangeException>();

    // Punto 3 — una espera fuera de [1 s, 1 h] no es válida.
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3601)]
    public void Espera_fuera_de_rango_lanza(int segundos) =>
        FluentActions.Invoking(() => new PoliticaReintentos(3, TimeSpan.FromSeconds(segundos))).Should().Throw<ArgumentOutOfRangeException>();

    // HU #14752 — factor fuera de [0, 0,5] o mínimo del límite fuera de [1 s, 10 min] no son válidos.
    [Theory]
    [InlineData(-0.01, 60)]
    [InlineData(0.51, 60)]
    [InlineData(0.2, 0)]
    [InlineData(0.2, 601)]
    public void Factor_o_minimo_del_limite_fuera_de_rango_lanza(double factor, int minimoSegundos) =>
        FluentActions.Invoking(() => new PoliticaReintentos(3, TimeSpan.FromMinutes(1), factor, TimeSpan.FromSeconds(minimoSegundos)))
            .Should().Throw<ArgumentOutOfRangeException>();

    // HU #14752 — el aleatorio va en [0, 1].
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Aleatorio_fuera_de_0_y_1_lanza(double aleatorio) =>
        FluentActions.Invoking(() => PorDefecto().Decidir("TIMEOUT", 0, DateTimeOffset.UnixEpoch, aleatorio)).Should().Throw<ArgumentOutOfRangeException>();
}

using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 3 (diseño §5.3 punto 6, ADR-0084) — el freno hacia Trámites (<see cref="FrenoTramites"/>, uno por réplica):
/// tras <c>PausaTrasFallos</c> transitorios SEGUIDOS abre una pausa de <c>PausaSegundos</c> en la que el consumidor espera antes de
/// llamar (y el individual responde 503 con <c>Retry-After</c>); un éxito la cierra y reinicia la cuenta. La tasa la pone un
/// <c>TokenBucketRateLimiter</c>: ráfaga = <c>Concurrencia</c> y <c>TasaPorSegundo</c> fichas por segundo. Reloj manual, sin red.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~FrenoTramitesTests
/// </code>
/// </example>
public sealed class FrenoTramitesTests
{
    private static OpcionesDerivacion Opciones(int pausaTrasFallos = 5, int pausaSegundos = 30, int tasa = 10, ushort concurrencia = 4) => new()
    {
        PausaTrasFallos = pausaTrasFallos,
        PausaSegundos = pausaSegundos,
        TasaPorSegundo = tasa,
        Concurrencia = concurrencia,
        Prefetch = Math.Max((ushort)8, concurrencia),
    };

    [Fact]
    public void Tras_N_transitorios_seguidos_abre_la_pausa_y_un_exito_la_cierra()
    {
        var reloj = new RelojManual();
        using var freno = new FrenoTramites(Opciones(), reloj, autoReponer: false);

        for (var i = 0; i < 4; i++)
            freno.RegistrarFalloTransitorio();
        freno.EnPausa.Should().BeFalse("cuatro fallos no llegan al umbral de cinco");

        freno.RegistrarFalloTransitorio();
        freno.EnPausa.Should().BeTrue();
        freno.PausaRestante().Should().Be(TimeSpan.FromSeconds(30));
        reloj.Avanzar(TimeSpan.FromSeconds(10));
        freno.PausaRestante().Should().Be(TimeSpan.FromSeconds(20));
        freno.RetryAfterSegundos().Should().Be(20, "el individual responde 503 con lo que falta de la pausa");

        freno.RegistrarExito();
        freno.EnPausa.Should().BeFalse("un éxito cierra la pausa");
        freno.PausaRestante().Should().BeNull();
    }

    [Fact]
    public void Un_exito_en_medio_reinicia_la_cuenta_de_fallos_seguidos()
    {
        using var freno = new FrenoTramites(Opciones(pausaTrasFallos: 3), new RelojManual(), autoReponer: false);

        freno.RegistrarFalloTransitorio();
        freno.RegistrarFalloTransitorio();
        freno.RegistrarExito();
        freno.RegistrarFalloTransitorio();
        freno.RegistrarFalloTransitorio();

        freno.EnPausa.Should().BeFalse("los fallos no fueron seguidos");
        freno.RegistrarFalloTransitorio();
        freno.EnPausa.Should().BeTrue();
    }

    [Fact]
    public void La_pausa_vence_sola_al_cumplir_PausaSegundos()
    {
        var reloj = new RelojManual();
        using var freno = new FrenoTramites(Opciones(pausaTrasFallos: 1, pausaSegundos: 30), reloj, autoReponer: false);

        freno.RegistrarFalloTransitorio();
        reloj.Avanzar(TimeSpan.FromSeconds(30));

        freno.EnPausa.Should().BeFalse();
        freno.RetryAfterSegundos().Should().BeNull();
    }

    [Fact]
    public async Task Durante_la_pausa_el_turno_espera_y_sigue_cuando_vence()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual { TimersManuales = true };
        using var freno = new FrenoTramites(Opciones(pausaTrasFallos: 2, pausaSegundos: 30), reloj, autoReponer: false);
        freno.RegistrarFalloTransitorio();
        freno.RegistrarFalloTransitorio();

        var turno = freno.EsperarTurnoAsync(ct).AsTask();
        await reloj.EsperarTimersAsync(1, ct);

        turno.IsCompleted.Should().BeFalse("con la pausa abierta el consumidor no llama a Trámites");
        reloj.Avanzar(TimeSpan.FromSeconds(30));
        await turno.WaitAsync(TimeSpan.FromSeconds(10), ct);
        turno.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task La_tasa_deja_pasar_una_rafaga_igual_a_la_concurrencia_y_luego_espera_la_reposicion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freno = new FrenoTramites(Opciones(tasa: 10, concurrencia: 4), new RelojManual(), autoReponer: false);

        for (var i = 0; i < 4; i++)
            await freno.EsperarTurnoAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct);
        var quinto = freno.EsperarTurnoAsync(ct).AsTask();
        await Task.Delay(50, ct);

        quinto.IsCompleted.Should().BeFalse("la ráfaga es igual a la concurrencia; la quinta llamada espera ficha");
        await Task.Delay(freno.PeriodoReposicion + TimeSpan.FromMilliseconds(50), ct);
        freno.Reponer().Should().BeTrue();
        await quinto.WaitAsync(TimeSpan.FromSeconds(5), ct);
        (freno.PeriodoReposicion, freno.FichasPorPeriodo).Should().Be((TimeSpan.FromMilliseconds(100), 1), "TasaPorSegundo = 10 → una ficha cada 100 ms");
    }

    [Fact]
    public void Con_tasas_altas_el_periodo_no_baja_de_10_ms_y_entran_varias_fichas()
    {
        using var freno = new FrenoTramites(Opciones(tasa: 10_000), new RelojManual(), autoReponer: false);

        (freno.PeriodoReposicion, freno.FichasPorPeriodo).Should().Be((TimeSpan.FromMilliseconds(10), 100), "el temporizador del limitador no repite periodos menores que 1 ms");
    }
}

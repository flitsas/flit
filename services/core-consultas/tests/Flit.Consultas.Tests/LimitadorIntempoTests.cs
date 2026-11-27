using Flit.Consultas.Api.RuntIntempo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #14752 (D5 = a, contrato-runt §3.2) — tope hacia Intempo por instancia de core-consultas: como mucho
/// <c>INTEMPO_API_MAX_CONCURRENT</c> peticiones a la vez (por defecto 4) y <c>INTEMPO_API_MAX_REQUESTS_PER_SECOND</c> por segundo
/// (por defecto 4). Primero la concurrencia y después el token. Con <c>autoReponer=false</c> los tokens solo vuelven con
/// <see cref="LimitadorIntempo.Reponer"/> (que repone lo correspondiente al tiempo transcurrido: un periodo completo, 4).
/// <para>Uso de ejemplo:</para>
/// <code>
/// await using var permiso = await limitador.AdquirirAsync(ct); // espera turno y token
/// </code>
/// </summary>
public sealed class LimitadorIntempoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IntempoOpciones Opciones(int concurrentes, int porSegundo) => IntempoOpciones.Desde(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["INTEMPO_API_BASE_URL"] = "https://intempo.prueba.local",
            ["INTEMPO_API_MAX_CONCURRENT"] = concurrentes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["INTEMPO_API_MAX_REQUESTS_PER_SECOND"] = porSegundo.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build());

    [Fact]
    public async Task Diez_adquisiciones_en_paralelo_nunca_tienen_mas_de_4_a_la_vez()
    {
        // Muchos tokens por segundo: aquí solo cuenta la concurrencia.
        await using var limitador = new LimitadorIntempo(Opciones(concurrentes: 4, porSegundo: 100));
        var dentro = 0;
        var maximo = 0;
        var candado = new object();

        async Task Trabajo()
        {
            await using var permiso = await limitador.AdquirirAsync(Ct);
            lock (candado)
                maximo = Math.Max(maximo, ++dentro);
            await Task.Delay(TimeSpan.FromMilliseconds(40), Ct);
            lock (candado)
                dentro--;
        }

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(Trabajo, Ct)));

        maximo.Should().BeLessThanOrEqualTo(4).And.BeGreaterThan(1, "las peticiones sí corren en paralelo hasta el tope");
        dentro.Should().Be(0);
    }

    [Fact]
    public async Task Con_4_por_segundo_la_quinta_espera_hasta_reponer()
    {
        await using var limitador = new LimitadorIntempo(Opciones(concurrentes: 4, porSegundo: 4), autoReponer: false);
        for (var i = 0; i < 4; i++)
            await (await limitador.AdquirirAsync(Ct)).DisposeAsync();

        var quinta = limitador.AdquirirAsync(Ct).AsTask();
        // Pasado más de un periodo, sin reposición automática la quinta sigue esperando su token. (TryReplenish repone en
        // proporción al tiempo transcurrido desde la última reposición, así que la prueba deja pasar el periodo completo.)
        await Task.Delay(TimeSpan.FromMilliseconds(1200), Ct);
        quinta.IsCompleted.Should().BeFalse("los 4 tokens del segundo se gastaron y nadie los repuso: la quinta espera");

        limitador.Reponer();
        var permiso = await quinta.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await permiso.DisposeAsync();
    }

    [Fact]
    public async Task La_cancelacion_libera_la_espera()
    {
        await using var limitador = new LimitadorIntempo(Opciones(concurrentes: 1, porSegundo: 4), autoReponer: false);
        var primero = await limitador.AdquirirAsync(Ct);
        using var cancelar = new CancellationTokenSource();

        var enEspera = limitador.AdquirirAsync(cancelar.Token).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        enEspera.IsCompleted.Should().BeFalse("la única plaza está ocupada");
        await cancelar.CancelAsync();

        await FluentActions.Awaiting(() => enEspera).Should().ThrowAsync<OperationCanceledException>();
        await primero.DisposeAsync();
        // La espera cancelada no se quedó con la plaza ni con un token: la siguiente entra sin reponer.
        var siguiente = await limitador.AdquirirAsync(Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await siguiente.DisposeAsync();
    }
}

using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13727 (F03-H2), AC3 — lease de la cola durable con reloj inyectable (<see cref="RelojManual"/>, sin esperas reales): un
/// procesador toma 2 de las 4 celdas y muere a mitad (se cancela con las celdas EN_CURSO); al vencer el lease, otro procesador
/// las retoma y ningún vehículo queda sin procesar. Con el lease vigente nadie las retoma.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~LeaseTests
/// </code>
/// </example>
public sealed class LeaseTests
{
    // AC3 (F03-C2) — lease vencido tras la caída: el segundo procesador retoma las EN_CURSO y termina el lote.
    [Fact]
    public async Task Celda_EN_CURSO_con_lease_vencido_se_retoma_tras_reinicio()
    {
        var ct = TestContext.Current.CancellationToken;
        var (reloj, ejecutor, api) = await ArrancarAsync();
        await using var _ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await Motor.LoteConfirmadoAsync(api, empresa, Motor.Placas(4), ct);
        var enCurso = await MorirAMitadAsync(api, ejecutor, ct);

        reloj.Avanzar(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        ejecutor.Comportamiento = (_, _) => Task.FromResult(ResultadoCelda.Completado("{}", DateTimeOffset.UtcNow));
        await Motor.DrenarAsync(api, Motor.Procesador(api, tamanoLote: 10), ct);

        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        celdas.Should().HaveCount(4).And.OnlyContain(c => c.Estado == EstadoCelda.Completado && c.LeaseHasta == null, "ningún vehículo queda sin procesar");
        enCurso.Should().OnlyContain(celda => ejecutor.Llamadas[celda] == 2, "la que murió en el primer procesador se retomó una vez");
        celdas.Select(c => c.Id).Except(enCurso).Should().OnlyContain(celda => ejecutor.Llamadas[celda] == 1);
    }

    // AC3 (F03-C2) — con el lease vigente (pasó 1 de los 2 minutos) el otro procesador solo toma las PENDIENTE.
    [Fact]
    public async Task Celda_con_lease_vigente_no_se_retoma()
    {
        var ct = TestContext.Current.CancellationToken;
        var (reloj, ejecutor, api) = await ArrancarAsync();
        await using var _ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await Motor.LoteConfirmadoAsync(api, empresa, Motor.Placas(4), ct);
        var enCurso = await MorirAMitadAsync(api, ejecutor, ct);
        var leases = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Where(c => enCurso.Contains(c.Id)).ToDictionary(c => c.Id, c => c.LeaseHasta);

        reloj.Avanzar(TimeSpan.FromMinutes(1));
        ejecutor.Comportamiento = (_, _) => Task.FromResult(ResultadoCelda.Completado("{}", DateTimeOffset.UtcNow));
        var tomadas = await Motor.Procesador(api, tamanoLote: 10).ProcesarLoteAsync(ct);

        tomadas.Should().Be(2, "solo las 2 PENDIENTE");
        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        celdas.Where(c => enCurso.Contains(c.Id)).Should().OnlyContain(c => c.Estado == EstadoCelda.EnCurso && c.LeaseHasta == leases[c.Id]);
        enCurso.Should().OnlyContain(celda => ejecutor.Llamadas[celda] == 1, "nadie la retomó");
        celdas.Where(c => !enCurso.Contains(c.Id)).Should().OnlyContain(c => c.Estado == EstadoCelda.Completado);
    }

    private static async Task<(RelojManual Reloj, EjecutorDePrueba Ejecutor, ApiDePrueba Api)> ArrancarAsync()
    {
        var reloj = new RelojManual();
        var ejecutor = new EjecutorDePrueba();
        var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            ejecutor.Registrar(b.Services);
        });
        return (reloj, ejecutor, api);
    }

    /// <summary>
    /// Un procesador con lote de 2 toma 2 celdas, el ejecutor se queda colgado y el proceso «muere» (se cancela): quedan 2
    /// EN_CURSO con lease y 2 PENDIENTE. Devuelve las EN_CURSO.
    /// </summary>
    private static async Task<IReadOnlyList<Guid>> MorirAMitadAsync(ApiDePrueba api, EjecutorDePrueba ejecutor, CancellationToken ct)
    {
        var dentro = 0;
        var dosDentro = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ejecutor.Comportamiento = async (_, token) =>
        {
            if (Interlocked.Increment(ref dentro) == 2)
                dosDentro.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return ResultadoCelda.Error();
        };
        using var muerte = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var primero = Motor.Procesador(api, tamanoLote: 2).ProcesarLoteAsync(muerte.Token);
        await dosDentro.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

        await muerte.CancelAsync();
        var esperar = async () => await primero;
        await esperar.Should().ThrowAsync<OperationCanceledException>();

        var enCurso = ejecutor.Llamadas.Keys.ToList();
        enCurso.Should().HaveCount(2);
        (await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.consultas WHERE estado = 'EN_CURSO' AND lease_hasta IS NOT NULL", ct)).Should().Be(2);
        (await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.consultas WHERE estado = 'PENDIENTE'", ct)).Should().Be(2);
        return enCurso;
    }
}

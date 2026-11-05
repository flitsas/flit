using System.Net;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13727 (ADR-0075, T9; reemplaza a <c>ColaConcurrenciaTests</c> y <c>LeaseTests</c>) — sin cola en Postgres ni lease, la
/// celda se «toma» por su trabajo: cada celda publicada tiene un <c>trabajo_id</c> vigente y solo un resultado de ese trabajo la
/// cierra. Con resultados entregados en paralelo (concurrencia 4 del consumidor) cada celda se cierra una sola vez, cada resultado
/// llega con la empresa de su celda fijada en el contexto, y un resultado repetido del mismo trabajo no la vuelve a cerrar. Un
/// reproceso le da un trabajo nuevo: el resultado tardío del anterior ya no la toca. PostgreSQL real con el bus de prueba.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~TomaPorTrabajoTests
/// </code>
/// </example>
public sealed class TomaPorTrabajoTests
{
    // 50 vehículos de A y 3 de B, todos los resultados a la vez: cada celda se cierra una vez, con la empresa de su celda.
    [Fact]
    public async Task Resultados_en_paralelo_cierran_cada_celda_una_sola_vez_con_su_empresa()
    {
        var ct = TestContext.Current.CancellationToken;
        var ejecutor = new EjecutorDePrueba();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => ejecutor.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var loteA = await Motor.LoteConfirmadoAsync(api, empresas.A, Motor.Placas(50), ct);
        var loteB = await Motor.LoteConfirmadoAsync(api, empresas.B, Motor.Placas(3, desde: 51), ct);
        var bus = Motor.Bus(api);
        var trabajos = await bus.DescartarTrabajosAsync(ct);
        trabajos.Should().HaveCount(53, "un trabajo de RUNT por vehículo");
        var resultados = trabajos.Select(t => bus.Resultado(t, new RespuestaSimulada(null))).ToList();

        await Task.WhenAll(Particionar(resultados, 4).Select(async parte =>
        {
            foreach (var resultado in parte)
                await bus.EntregarAsync(resultado, ct);
        }));
        await bus.ProcesarTodoAsync(ct);

        var celdasA = await Motor.CeldasAsync(api, empresas.A.Id, loteA, ct);
        var todas = celdasA.Concat(await Motor.CeldasAsync(api, empresas.B.Id, loteB, ct)).ToList();
        todas.Should().HaveCount(53).And.OnlyContain(c => c.Estado == EstadoCelda.Completado);
        ejecutor.Llamadas.Keys.Should().BeEquivalentTo(todas.Select(c => c.Id));
        ejecutor.Contextos.Should().OnlyContain(c => c.EmpresaDelContexto == c.Tenant, "cada resultado fija la empresa de su sobre");
        // El cierre sube la versión del tablero por cada celda y la copia en la celda (base del sondeo de F04).
        (await Cabeceras.LeerAsync(api, empresas.A.Id, loteA, ct)).VersionTablero.Should().Be(51);
        celdasA.Select(c => c.VersionTablero).Should().OnlyHaveUniqueItems().And.OnlyContain(v => v >= 2 && v <= 51);
    }

    // El mismo trabajo respondido dos veces (dos eventos distintos, a la vez): la celda se cierra una sola vez.
    [Fact]
    public async Task Dos_resultados_del_mismo_trabajo_a_la_vez_cierran_la_celda_una_vez()
    {
        var ct = TestContext.Current.CancellationToken;
        var ejecutor = new EjecutorDePrueba();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => ejecutor.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await Motor.LoteConfirmadoAsync(api, empresa, Motor.Placas(1), ct);
        var bus = Motor.Bus(api);
        var trabajo = (await bus.DescartarTrabajosAsync(ct)).Single();

        await Task.WhenAll(
            bus.EntregarAsync(bus.Resultado(trabajo, new RespuestaSimulada(null)), ct),
            bus.EntregarAsync(bus.Resultado(trabajo, new RespuestaSimulada(null)), ct));
        await TerminarAsync(bus, ct);

        var celda = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        celda.Estado.Should().Be(EstadoCelda.Completado);
        (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle("un solo cierre deja un solo intento");
        (await Cabeceras.LeerAsync(api, empresa.Id, lote, ct)).VersionTablero.Should().Be(celda.VersionTablero).And.Be(2);
    }

    // Reproceso: trabajo_id nuevo en la misma transacción; el resultado tardío del trabajo anterior no toca la celda.
    [Fact]
    public async Task Tras_un_reproceso_el_resultado_del_trabajo_anterior_se_descarta()
    {
        var ct = TestContext.Current.CancellationToken;
        var ejecutor = new EjecutorDePrueba { Comportamiento = (_, _) => Task.FromResult(ResultadoCelda.Error("ESTADO_NO_OK")) };
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => ejecutor.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await Motor.LoteConfirmadoAsync(api, empresa, Motor.Placas(1), ct);
        var bus = Motor.Bus(api);
        var primero = (await bus.DescartarTrabajosAsync(ct)).Single();
        await bus.EntregarAsync(bus.Resultado(primero, new RespuestaSimulada(null)), ct);
        var celda = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        celda.Estado.Should().Be(EstadoCelda.Error);

        var reproceso = await api.Cliente(MotorPersonas.Admin(api, empresa)).PostAsync($"{Program.Prefijo}/consultas/{celda.Id}/reprocesar", null, ct);
        reproceso.StatusCode.Should().Be(HttpStatusCode.Accepted, await reproceso.Content.ReadAsStringAsync(ct));
        var segundo = (await bus.DescartarTrabajosAsync(ct)).Single();
        segundo.TrabajoId.Should().NotBe(primero.TrabajoId);

        ejecutor.Comportamiento = (_, _) => Task.FromResult(ResultadoCelda.Completado("{}", DateTimeOffset.UtcNow));
        (await bus.EntregarAsync(bus.Resultado(primero, new RespuestaSimulada(null)), ct)).Should().BeTrue("se confirma sin efecto");
        var enCurso = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        (enCurso.Estado, enCurso.TrabajoId).Should().Be((EstadoCelda.EnCurso, segundo.TrabajoId), "sigue esperando el trabajo del reproceso");

        await bus.EntregarAsync(bus.Resultado(segundo, new RespuestaSimulada(null)), ct);
        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado.Should().Be(EstadoCelda.Completado);
        ejecutor.Llamadas[celda.Id].Should().Be(2, "el resultado descartado no llegó al ejecutor");
    }

    private static async Task TerminarAsync(BusDePrueba bus, CancellationToken ct)
    {
        // Un conflicto de concurrencia deja el resultado en la cola de reintentos: se entrega cuando vence (reloj del sistema).
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (bus.Demorados > 0)
        {
            DateTime.UtcNow.Should().BeBefore(limite, "el resultado demorado debe entregarse");
            await bus.ProcesarLoteAsync(ct);
            await Task.Delay(100, ct);
        }
    }

    private static IEnumerable<List<T>> Particionar<T>(IReadOnlyList<T> elementos, int partes) =>
        Enumerable.Range(0, partes).Select(p => elementos.Where((_, i) => i % partes == p).ToList());
}

using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// HU #14576, carril H-8 (PostgreSQL real): las dos últimas celdas de un vehículo «Con análisis IA» cierran a la vez, cada una en su
/// scope y su transacción, como dos resultados que el consumidor atiende en paralelo. El cierre A queda sin confirmar; el cierre B
/// arranca y la prueba espera a que su sesión quede bloqueada por un lock (pg_stat_activity) antes de confirmar A. Así el orden es
/// determinista: sin serializar antes de la foto, B ya leyó a A EN_CURSO y ninguno de los dos ve el flanco ni termina el lote.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~CierreConcurrenteDictamenTests
/// </code>
/// </example>
public sealed class CierreConcurrenteDictamenTests
{
    [Fact]
    public async Task Dos_cierres_concurrentes_de_las_ultimas_celdas_escriben_un_dictamen_y_terminan_el_lote()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos:
        [
            new("TST881", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.EnCurso, 1), new(Fuente.SimitPropietario, EstadoCelda.EnCurso, 1)]),
        ], modo: ModoDiagnostico.ConAnalisis, tipoTramiteCodigo: "TRASPASO_STANDARD");
        var vehiculo = lote.Vehiculos[0].Id;
        var simitVehiculo = lote.Celdas.Single(c => c.Fuente == Fuente.SimitVehiculo).Id;
        var simitPropietario = lote.Celdas.Single(c => c.Fuente == Fuente.SimitPropietario).Id;

        // Cierre A: llega hasta el final de CerrarAsync y guarda, pero no confirma hasta que B quede esperando.
        var aGuardado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmarA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pidB = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scopeA = OutboxDePrueba.Scope(api, empresa.Id);
        await using var scopeB = OutboxDePrueba.Scope(api, empresa.Id);
        var cierreA = Task.Run(
            () => CerrarEnTransaccionAsync(scopeA, simitVehiculo, antes: null, antesDeConfirmar: async () =>
            {
                aGuardado.TrySetResult();
                await confirmarA.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }, ct),
            ct);
        await aGuardado.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

        // Cierre B: su propia sesión; la prueba espera a que Postgres la deje esperando un lock y solo entonces confirma A.
        var cierreB = Task.Run(() => CerrarEnTransaccionAsync(scopeB, simitPropietario, antes: pid => pidB.TrySetResult(pid), antesDeConfirmar: null, ct), ct);
        await EsperarBloqueoAsync(api.BaseDeDatos, await pidB.Task.WaitAsync(TimeSpan.FromSeconds(30), ct), cierreB, ct);
        confirmarA.TrySetResult();
        await Task.WhenAll(cierreA, cierreB).WaitAsync(TimeSpan.FromSeconds(30), ct);

        await using var db = empresa.Db();
        var dictamenes = await db.Set<DictamenVehiculo>().AsNoTracking().Where(d => d.VehiculoId == vehiculo).ToListAsync(ct);
        var cabecera = await db.Set<CabeceraDiagnostico>().AsNoTracking().SingleAsync(c => c.Id == lote.Id, ct);
        using var _ = new AssertionScope();
        dictamenes.Should().ContainSingle("el segundo cierre ve a su hermana confirmada y detecta el flanco una sola vez");
        dictamenes.Should().OnlyContain(d => d.Vigente);
        cabecera.Estado.Should().Be(EstadoDiagnostico.Completado, "con las tres celdas en COMPLETADO el recálculo termina el lote");
    }

    /// <summary>
    /// Un cierre como el del consumidor: scope propio, transacción dentro de la estrategia de ejecución, celda seguida y
    /// <see cref="PublicadorCeldas.CerrarAsync"/>. <paramref name="antes"/> recibe el pid de la sesión ya en la transacción;
    /// <paramref name="antesDeConfirmar"/> corre después de guardar y antes del commit.
    /// </summary>
    private static async Task CerrarEnTransaccionAsync(
        AsyncServiceScope scope, Guid celdaId, Action<int>? antes, Func<Task>? antesDeConfirmar, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var publicador = scope.ServiceProvider.GetRequiredService<PublicadorCeldas>();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct);
            antes?.Invoke(await db.Database.SqlQueryRaw<int>("select pg_backend_pid() as \"Value\"").SingleAsync(ct));
            var celda = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == celdaId, ct);
            await publicador.CerrarAsync(celda, ResultadoCelda.Completado("{}", DateTimeOffset.UtcNow), [], versionTablero: null, ct);
            await db.SaveChangesAsync(ct);
            if (antesDeConfirmar is not null)
                await antesDeConfirmar();
            await transaccion.CommitAsync(ct);
        });
    }

    /// <summary>Espera (tope 15 s) a que la sesión <paramref name="pid"/> quede esperando un lock; falla si el cierre termina o lanza antes.</summary>
    private static async Task EsperarBloqueoAsync(string baseDeDatos, int pid, Task cierre, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(baseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand("select wait_event_type from pg_stat_activity where pid = @pid", conexion);
        consulta.Parameters.AddWithValue("pid", pid);
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            if (cierre.IsCompleted)
            {
                await cierre;
                throw new InvalidOperationException("El cierre B terminó sin esperar el lock del cierre A.");
            }

            if (await consulta.ExecuteScalarAsync(ct) is string espera && espera == "Lock")
                return;
            DateTime.UtcNow.Should().BeBefore(limite, "el cierre B debe quedar esperando el lock del cierre A");
            await Task.Delay(20, ct);
        }
    }
}

using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13727 (F03-H2), AC4 — dos procesadores sobre la misma cola (mismo Postgres) no ejecutan dos veces la misma celda:
/// la toma usa <c>FOR UPDATE SKIP LOCKED</c> sobre <c>ix_cola</c>, entre empresas, y cada ejecución llega con la empresa de su
/// celda. También: una celda bloqueada por otra conexión no se toma, y la toma respeta los vehículos en vuelo por empresa.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ColaConcurrenciaTests
/// </code>
/// </example>
public sealed class ColaConcurrenciaTests
{
    // AC4 (F03-C3) — 50 vehículos de A (y 3 de B), dos procesadores en paralelo: llamadas = celdas, cada una una sola vez.
    [Fact]
    public async Task Dos_procesadores_en_paralelo_ejecutan_cada_celda_una_sola_vez()
    {
        var ct = TestContext.Current.CancellationToken;
        var ejecutor = new EjecutorDePrueba();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => ejecutor.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var loteA = await Motor.LoteConfirmadoAsync(api, empresas.A, Motor.Placas(50), ct);
        var loteB = await Motor.LoteConfirmadoAsync(api, empresas.B, Motor.Placas(3, desde: 51), ct);

        await Task.WhenAll(
            Motor.DrenarAsync(api, Motor.Procesador(api, tamanoLote: 4), ct),
            Motor.DrenarAsync(api, Motor.Procesador(api, tamanoLote: 4), ct));

        var celdasA = await Motor.CeldasAsync(api, empresas.A.Id, loteA, ct);
        var celdasB = await Motor.CeldasAsync(api, empresas.B.Id, loteB, ct);
        var todas = celdasA.Concat(celdasB).ToList();
        todas.Should().HaveCount(53).And.OnlyContain(c => c.Estado == EstadoCelda.Completado && c.LeaseHasta == null);
        ejecutor.Llamadas.Keys.Should().BeEquivalentTo(todas.Select(c => c.Id));
        ejecutor.Llamadas.Values.Should().OnlyContain(n => n == 1, "ninguna celda se ejecuta dos veces");
        ejecutor.Contextos.Should().OnlyContain(c => c.EmpresaDelContexto == c.Tenant, "cada ejecución fija la empresa de su celda");
        // El escritor sube la versión del tablero por cada celda cerrada y la copia en la celda (base del sondeo de F04).
        (await Cabeceras.LeerAsync(api, empresas.A.Id, loteA, ct)).VersionTablero.Should().Be(51);
        celdasA.Select(c => c.VersionTablero).Should().OnlyHaveUniqueItems().And.OnlyContain(v => v >= 2 && v <= 51);
    }

    // AC4 (F03-C3) — SKIP LOCKED con dos conexiones abiertas: la fila que otra transacción tiene bloqueada no se toma.
    [Fact]
    public async Task Celda_bloqueada_por_un_procesador_no_la_toma_el_otro()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await Motor.LoteConfirmadoAsync(api, empresa, Motor.Placas(2), ct);
        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        var bloqueada = celdas[0].Id;
        var libre = celdas[1].Id;

        await using var otra = new NpgsqlConnection(api.BaseDeDatos);
        await otra.OpenAsync(ct);
        await using (var transaccion = await otra.BeginTransactionAsync(ct))
        {
            await using var bloqueo = new NpgsqlCommand("SELECT id FROM diagnostico.consultas WHERE id = @id FOR UPDATE", otra, transaccion);
            bloqueo.Parameters.AddWithValue("id", bloqueada);
            await bloqueo.ExecuteScalarAsync(ct);

            (await TomarAsync(api, ct)).Select(c => c.Id).Should().Equal([libre], "la bloqueada se salta sin esperar");
            await transaccion.RollbackAsync(ct);
        }

        (await TomarAsync(api, ct)).Select(c => c.Id).Should().Equal([bloqueada], "liberada, se toma; la otra tiene lease vigente");
    }

    // Concurrencia acotada por compañía (plan §10): la toma no pasa de N vehículos en vuelo por empresa, y una empresa llena
    // no frena a la otra.
    [Fact]
    public async Task Toma_no_supera_los_vehiculos_en_vuelo_por_empresa()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await Motor.LoteConfirmadoAsync(api, empresas.A, Motor.Placas(8), ct);
        await Motor.LoteConfirmadoAsync(api, empresas.B, Motor.Placas(2, desde: 9), ct);

        var primera = await TomarAsync(api, ct, vehiculosPorEmpresa: 3);

        primera.Count(c => c.TenantId == empresas.A.Id).Should().Be(3);
        primera.Count(c => c.TenantId == empresas.B.Id).Should().Be(2);
        (await TomarAsync(api, ct, vehiculosPorEmpresa: 3)).Should().BeEmpty("A ya tiene 3 vehículos en vuelo y B no tiene más");
    }

    private static async Task<IReadOnlyList<CeldaTomada>> TomarAsync(ApiDePrueba api, CancellationToken ct, int vehiculosPorEmpresa = 5)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var cola = ActivatorUtilities.CreateInstance<ColaConsultas>(scope.ServiceProvider);
        return await cola.TomarAsync(20, TimeSpan.FromMinutes(2), vehiculosPorEmpresa, [Fuente.Runt], ct);
    }
}

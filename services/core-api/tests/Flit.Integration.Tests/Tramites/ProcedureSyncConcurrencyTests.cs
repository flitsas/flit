using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13083 (Feature #13066, Épica #12737) AC1 — sin omisiones bajo concurrencia real: escritores en paralelo
/// cambian trámites en transacciones que toman su versión y confirman después de otras (orden de confirmación
/// distinto al de versión), mientras un lector recorre el feed con ventana de estabilidad, como lo hará Flito.
/// Al terminar, la última versión confirmada de CADA trámite tiene que haberse entregado.
/// </summary>
public sealed class ProcedureSyncConcurrencyTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const int Tramites = 40;
    private const int Escritores = 8;
    private const int CambiosPorEscritor = 30;
    private static readonly TimeSpan Ventana = TimeSpan.FromMilliseconds(300);
    private static readonly Guid CompaniaA = new("5a5a5a5a-0001-4000-8000-000000013083");
    private static readonly Guid CompaniaB = new("5a5a5a5a-0002-4000-8000-000000013083");
    private static readonly Guid Gestor = new("5a5a5a5a-0003-4000-8000-000000013083");

    [PostgresFact]
    public async Task AC1_NingunCambioConfirmadoQuedaSinEntregarAunqueLasTransaccionesConfirmenFueraDeOrden()
    {
        await SembrarAsync();
        var ids = new List<Guid>();
        for (var n = 1; n <= Tramites; n++)
        {
            ids.Add(await RadicadoAsync(n % 2 == 0 ? CompaniaA : CompaniaB, n));
        }

        var entregado = new Dictionary<Guid, long>();
        var cursor = await RecorrerAsync(null, entregado); // estado inicial
        using var fin = new CancellationTokenSource();
        var paginasDuranteEscritura = 0;

        // Lector concurrente: recorre sin parar mientras escriben.
        var lector = Task.Run(async () =>
        {
            while (!fin.IsCancellationRequested)
            {
                cursor = await RecorrerAsync(cursor, entregado);
                paginasDuranteEscritura++;
                await Task.Delay(20);
            }
        });

        // Escritores: cada cambio toma su versión (UPDATE o INSERT en una tabla hija) y confirma tras una
        // espera aleatoria; mientras tanto otros confirman versiones mayores.
        var escritores = Enumerable.Range(0, Escritores).Select(w => Task.Run(async () =>
        {
            var azar = new Random(13083 + w);
            for (var m = 0; m < CambiosPorEscritor; m++)
            {
                var id = ids[azar.Next(ids.Count)];
                await using var conn = await Fixture.OpenConnectionAsync();
                await using var tx = await conn.BeginTransactionAsync();
                var sql = azar.Next(2) == 0
                    ? "UPDATE tramites.procedure_instances SET status = status WHERE id = @id"
                    : "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) "
                      + "SELECT tenant_id, id, 'entregado' FROM tramites.procedure_instances WHERE id = @id";
                await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    await cmd.ExecuteNonQueryAsync();
                }

                await using (var pausa = new NpgsqlCommand("SELECT pg_sleep(@s)", conn, tx))
                {
                    pausa.Parameters.AddWithValue("s", azar.Next(0, 60) / 1000.0);
                    await pausa.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }
        })).ToList();

        await Task.WhenAll(escritores);
        await fin.CancelAsync();
        await lector;

        // Tras la ventana, lo que quede por entregar llega; luego el feed queda quieto.
        await Task.Delay(Ventana * 2, TestContext.Current.CancellationToken);
        cursor = await RecorrerAsync(cursor, entregado);
        (await LeerAsync(ProcedureSyncPageRequest.FromCursor(cursor!.Value, 1000, Ventana))).Should().BeEmpty("el feed quedó al día");

        var finales = await VersionesFinalesAsync();
        paginasDuranteEscritura.Should().BePositive("el lector recorrió mientras se escribía");
        finales.Should().HaveCount(Tramites);
        foreach (var (id, version) in finales)
        {
            entregado.Should().ContainKey(id);
            entregado[id].Should().Be(version, $"la última versión confirmada de {id} tiene que haberse entregado");
        }
    }

    /// <summary>
    /// DDL 128 — el sello de sincronización no toma el bloqueo de clave. Una transacción retiene FOR KEY SHARE sobre
    /// el trámite (lo que hace la FK de cualquier INSERT en una tabla hija) y otra lo cambia: con el índice único
    /// corriente de sync_version (DDL 124) el UPDATE pedía FOR UPDATE y quedaba esperando; ese cruce, con dos
    /// escrituras en hijas del mismo trámite, era un deadlock. Con el índice parcial pasa sin esperar.
    /// </summary>
    [PostgresFact]
    public async Task ElSelloDeSincronizacionNoEsperaAlBloqueoDeLaClaveForanea()
    {
        await SembrarAsync();
        var id = await RadicadoAsync(CompaniaA, 1);

        await using var connHija = await Fixture.OpenConnectionAsync();
        await using var txHija = await connHija.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var cmd = new NpgsqlCommand("SELECT 1 FROM tramites.procedure_instances WHERE id = @id FOR KEY SHARE", connHija, txHija))
        {
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var conn = await Fixture.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var espera = new NpgsqlCommand("SET LOCAL lock_timeout = '2s'", conn, tx))
        {
            await espera.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var sello = new NpgsqlCommand("UPDATE tramites.procedure_instances SET status = status WHERE id = @id", conn, tx);
        sello.Parameters.AddWithValue("id", id);
        await FluentActions.Awaiting(() => sello.ExecuteNonQueryAsync(TestContext.Current.CancellationToken))
            .Should().NotThrowAsync("el sello toma FOR NO KEY UPDATE, compatible con FOR KEY SHARE");
        await tx.CommitAsync(TestContext.Current.CancellationToken);
        await txHija.RollbackAsync(TestContext.Current.CancellationToken);

        await using var indice = new NpgsqlCommand(
            "SELECT indpred IS NOT NULL FROM pg_index WHERE indexrelid = 'tramites.uq_procedure_instances_sync_version'::regclass", conn);
        ((bool)(await indice.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).Should().BeTrue("el índice único es parcial");
    }

    /// <summary>Recorre desde <paramref name="desde"/> hasta agotar y guarda la mayor versión entregada por trámite.</summary>
    private async Task<ProcedureSyncPosition?> RecorrerAsync(ProcedureSyncPosition? desde, Dictionary<Guid, long> entregado)
    {
        var cursor = desde;
        while (true)
        {
            var pagina = await LeerAsync(new ProcedureSyncPageRequest(cursor, null, 7, Ventana));
            foreach (var cambio in pagina)
            {
                lock (entregado)
                {
                    entregado[cambio.ProcedureInstanceId] = Math.Max(entregado.GetValueOrDefault(cambio.ProcedureInstanceId), cambio.Position.Version);
                }
            }

            if (pagina.Count == 0)
            {
                return cursor;
            }

            cursor = pagina[^1].Position;
        }
    }

    private async Task<IReadOnlyList<ProcedureSyncChange>> LeerAsync(ProcedureSyncPageRequest request) =>
        await new ProcedureSyncReadRepository(NewContext()).ReadChangesAsync(request, CancellationToken.None);

    private async Task<Dictionary<Guid, long>> VersionesFinalesAsync()
    {
        var finales = new Dictionary<Guid, long>();
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT id, sync_version FROM tramites.procedure_instances WHERE tenant_id IN (@a, @b)", conn);
        cmd.Parameters.AddWithValue("a", CompaniaA);
        cmd.Parameters.AddWithValue("b", CompaniaB);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            finales[reader.GetGuid(0)] = reader.GetInt64(1);
        }

        return finales;
    }

    private async Task SembrarAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(CompaniaA, "IT-13083-A", isGroupParent: false, parentId: null));
        ctx.Tenants.Add(TenantSeed.New(CompaniaB, "IT-13083-B", isGroupParent: false, parentId: null));
        await ctx.SaveChangesAsync();
        ctx.Users.Add(new User
        {
            Id = Gestor,
            Email = "it-13083@flit.test",
            DisplayName = "Gestor 13083",
            Status = "active",
            HomeTenantId = CompaniaA,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> RadicadoAsync(Guid tenant, int n)
    {
        var id = Guid.CreateVersion7();
        await using (var ctx = NewContext())
        {
            var tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();
            ctx.ProcedureInstances.Add(new ProcedureInstance
            {
                Id = id,
                TenantId = tenant,
                ProcedureTypeId = tipo,
                ReferenceNumber = $"IT13083-{n}",
                Status = TramiteEstado.Borrador,
                Vin = $"VIN13083{n:D9}",
                CreatedByUserId = Gestor,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) "
            + "SELECT tenant_id, id, 'preasignacion' FROM tramites.procedure_instances WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }
}

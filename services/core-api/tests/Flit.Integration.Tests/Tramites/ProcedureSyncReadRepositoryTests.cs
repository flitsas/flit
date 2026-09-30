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
/// HU #13076 (Feature #13066, Épica #12737) contra Postgres real con TODAS las migraciones: la lectura
/// del feed recorre trámites de varias compañías por (transacción, versión) (AC1), respeta la ventana de
/// estabilidad (AC2), entrega solo radicados y no los suelta si retroceden (AC3), excluye los migrados
/// desde FLIT 1 (AC5) y NO pierde el cambio de una transacción larga que confirma después de otra con
/// versión mayor (riesgo señalado en la HU).
/// </summary>
public sealed class ProcedureSyncReadRepositoryTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid CompaniaA = new("5a5a5a5a-0001-4000-8000-000000013076");
    private static readonly Guid CompaniaB = new("5a5a5a5a-0002-4000-8000-000000013076");
    private static readonly Guid Gestor = new("5a5a5a5a-0003-4000-8000-000000013076");
    private static readonly TimeSpan SinVentana = TimeSpan.Zero;

    [PostgresFact]
    public async Task AC1_LeeTramitesDeVariasCompaniasEnOrdenYPorPaginas()
    {
        await SembrarCompaniasAsync();
        var t1 = await RadicadoAsync(CompaniaA, 1);
        var t2 = await RadicadoAsync(CompaniaB, 2);
        var t3 = await RadicadoAsync(CompaniaA, 3);

        var todo = await LeerAsync(new(null, null, 100, SinVentana));
        todo.Select(c => c.ProcedureInstanceId).Should().Equal(t1, t2, t3);
        todo.Select(c => c.TenantId).Should().Equal(CompaniaA, CompaniaB, CompaniaA);
        todo.Select(c => c.Position.Version).Should().BeInAscendingOrder();

        var pagina = await LeerAsync(ProcedureSyncPageRequest.FromCursor(todo[0].Position, 2, SinVentana));

        pagina.Select(c => c.ProcedureInstanceId).Should().Equal(t2, t3);
        (await LeerAsync(ProcedureSyncPageRequest.FromCursor(pagina[^1].Position, 2, SinVentana))).Should().BeEmpty();
    }

    [PostgresFact]
    public async Task AC2_UnCambioDeHaceMenosDe5SegundosNoSeEntregaTodavia()
    {
        await SembrarCompaniasAsync();
        var tramite = await RadicadoAsync(CompaniaA, 1);
        var ventana = TimeSpan.FromSeconds(5);

        (await LeerAsync(new(null, null, 100, ventana))).Should().BeEmpty();

        await Task.Delay(TimeSpan.FromSeconds(5.5), TestContext.Current.CancellationToken);

        (await LeerAsync(new(null, null, 100, ventana))).Select(c => c.ProcedureInstanceId).Should().Equal(tramite);
    }

    [PostgresFact]
    public async Task AC3_SoloRadicadosYElQueRetrocedeSigueDentro()
    {
        await SembrarCompaniasAsync();
        var nuncaRadicado = await TramiteAsync(CompaniaA, 1);
        await HistorialAsync(nuncaRadicado, "borrador");
        var retrocede = await RadicadoAsync(CompaniaB, 2);
        var antes = (await LeerAsync(new(null, null, 100, SinVentana))).Single(c => c.ProcedureInstanceId == retrocede);

        await EjecutarAsync(
            "UPDATE tramites.procedure_instances SET status = 'borrador' WHERE id = @id", retrocede);
        await HistorialAsync(retrocede, "borrador");

        var despues = await LeerAsync(ProcedureSyncPageRequest.FromCursor(antes.Position, 100, SinVentana));

        despues.Select(c => c.ProcedureInstanceId).Should().Equal(retrocede);
        (await LeerAsync(new(null, null, 100, SinVentana))).Should().NotContain(c => c.ProcedureInstanceId == nuncaRadicado);
    }

    [PostgresFact]
    public async Task AC5_LosMigradosDesdeFlit1NoAparecenNiComoBorrado()
    {
        await SembrarCompaniasAsync();
        var propio = await RadicadoAsync(CompaniaA, 1);
        var migrado = await RadicadoAsync(CompaniaA, 2);
        var migradoBorrado = await RadicadoAsync(CompaniaB, 3);
        await EjecutarAsync("UPDATE tramites.procedure_instances SET is_migrated = true WHERE id = @id", migrado);
        await EjecutarAsync(
            "UPDATE tramites.procedure_instances SET is_migrated = true, deleted_at = now() WHERE id = @id", migradoBorrado);

        var ids = (await LeerAsync(new(null, null, 100, SinVentana))).Select(c => c.ProcedureInstanceId);

        ids.Should().Equal(propio);
    }

    [PostgresFact]
    public async Task ElBorradoDeUnRadicadoPropioLlegaComoMarcaDeBorrado()
    {
        await SembrarCompaniasAsync();
        var tramite = await RadicadoAsync(CompaniaA, 1);
        await EjecutarAsync("UPDATE tramites.procedure_instances SET deleted_at = now() WHERE id = @id", tramite);

        var cambio = (await LeerAsync(new(null, null, 100, SinVentana))).Single();

        cambio.ProcedureInstanceId.Should().Be(tramite);
        cambio.IsDeleted.Should().BeTrue();
    }

    [PostgresFact]
    public async Task UnaTransaccionLargaNoSePierdeAunqueOtraConVersionMayorConfirmeAntes()
    {
        await SembrarCompaniasAsync();
        var largo = await RadicadoAsync(CompaniaA, 1);
        var corto = await RadicadoAsync(CompaniaB, 2);
        var cursor = (await LeerAsync(new(null, null, 100, SinVentana)))[^1].Position;

        // La transacción larga toma su versión primero y confirma al final.
        await using var connLarga = await Fixture.OpenConnectionAsync();
        await using var txLarga = await connLarga.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var cmd = new NpgsqlCommand(
            "UPDATE tramites.procedure_instances SET status = status WHERE id = @id", connLarga, txLarga))
        {
            cmd.Parameters.AddWithValue("id", largo);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await EjecutarAsync("UPDATE tramites.procedure_instances SET status = status WHERE id = @id", corto);

        // Con la larga abierta, el cambio corto (versión mayor, ya confirmado) todavía NO se entrega:
        // un cursor solo por versión lo habría entregado y habría dejado atrás al largo para siempre.
        // Sin esperar a la estabilidad: la propia transacción larga la retiene.
        (await LeerSinEsperarAsync(ProcedureSyncPageRequest.FromCursor(cursor, 100, SinVentana))).Should().BeEmpty();

        await txLarga.CommitAsync(TestContext.Current.CancellationToken);

        var entregados = await LeerAsync(ProcedureSyncPageRequest.FromCursor(cursor, 100, SinVentana));
        entregados.Select(c => c.ProcedureInstanceId).Should().Equal(largo, corto);
        entregados[0].Position.Version.Should().BeLessThan(entregados[1].Position.Version,
            "el largo tiene la versión menor aunque confirmó después: el caso que un cursor por versión pierde");
    }

    [PostgresFact]
    public async Task ElArranquePorFechaSoloEntregaLoCambiadoDesdeEntonces()
    {
        await SembrarCompaniasAsync();
        await RadicadoAsync(CompaniaA, 1);
        var desde = new DateTimeOffset(await EscalarAsync<DateTime>("SELECT clock_timestamp()"), TimeSpan.Zero);
        var nuevo = await RadicadoAsync(CompaniaB, 2);

        var ids = (await LeerAsync(ProcedureSyncPageRequest.FromSince(desde, 100, SinVentana))).Select(c => c.ProcedureInstanceId);

        ids.Should().Equal(nuevo);
    }

    [PostgresFact]
    public async Task CursorYFechaJuntosOPaginaFueraDeRango_SeRechazan()
    {
        var repo = new ProcedureSyncReadRepository(NewContext());

        await FluentActions.Awaiting(() => repo.ReadChangesAsync(new(new(1, 1), DateTimeOffset.UtcNow, 10, SinVentana)))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => repo.ReadChangesAsync(new(null, null, 1002, SinVentana)))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => repo.ReadChangesAsync(new(null, null, 0, SinVentana)))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // ── Siembra y utilidades ────────────────────────────────────────────────

    private async Task<IReadOnlyList<ProcedureSyncChange>> LeerAsync(ProcedureSyncPageRequest request)
    {
        await ProcedureSyncTestWait.EsperarFeedEstableAsync(Fixture);
        return await LeerSinEsperarAsync(request);
    }

    private Task<IReadOnlyList<ProcedureSyncChange>> LeerSinEsperarAsync(ProcedureSyncPageRequest request) =>
        new ProcedureSyncReadRepository(NewContext()).ReadChangesAsync(request, TestContext.Current.CancellationToken);

    private async Task SembrarCompaniasAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(CompaniaA, "IT-13076-A", isGroupParent: false, parentId: null));
        ctx.Tenants.Add(TenantSeed.New(CompaniaB, "IT-13076-B", isGroupParent: false, parentId: null));
        await ctx.SaveChangesAsync();
        ctx.Users.Add(new User
        {
            Id = Gestor,
            Email = "it-13076@flit.test",
            DisplayName = "Gestor 13076",
            Status = "active",
            HomeTenantId = CompaniaA,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> TramiteAsync(Guid tenant, int n)
    {
        await using var ctx = NewContext();
        var tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();
        var id = Guid.CreateVersion7();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = tipo,
            ReferenceNumber = $"IT13076-{n}",
            Status = TramiteEstado.Borrador,
            Vin = $"VIN13076{n:D9}",
            CreatedByUserId = Gestor,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    /// <summary>Trámite con una transición a preasignación en el historial (radicado al menos una vez).</summary>
    private async Task<Guid> RadicadoAsync(Guid tenant, int n)
    {
        var id = await TramiteAsync(tenant, n);
        await HistorialAsync(id, "preasignacion");
        return id;
    }

    private Task HistorialAsync(Guid tramite, string estado) =>
        EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) "
            + $"SELECT tenant_id, id, '{estado}' FROM tramites.procedure_instances WHERE id = @id",
            tramite);

    private async Task EjecutarAsync(string sql, Guid id)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<T> EscalarAsync<T>(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}

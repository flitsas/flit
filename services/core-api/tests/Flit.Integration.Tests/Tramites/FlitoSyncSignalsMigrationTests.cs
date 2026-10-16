using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13622 (Feature #13616, Épica #13614) contra Postgres real con TODAS las migraciones: cada versión nueva de
/// sincronización deja, en la misma transacción, un aviso pendiente para FLITO en
/// <c>integrations.flito_sync_signals</c>, uno por trámite (coalescente), con tombstone y sin migrados.
/// </summary>
public sealed class FlitoSyncSignalsMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Cliente = new("5a5a5a5a-0001-4000-8000-000000013622");
    private static readonly Guid Gestor = new("5a5a5a5a-0002-4000-8000-000000013622");
    private static readonly Guid Tramite = new("5a5a5a5a-0003-4000-8000-000000013622");
    private static readonly Guid Migrado = new("5a5a5a5a-0004-4000-8000-000000013622");

    [PostgresFact]
    public async Task AC1_CrearElTramiteDejaUnAvisoPendienteConSuVersion()
    {
        await SembrarAsync();

        var aviso = await AvisoAsync(Tramite);
        aviso.Should().NotBeNull();
        aviso!.SyncVersion.Should().Be(await EscalarAsync<long>("SELECT sync_version FROM tramites.procedure_instances WHERE id = @id"));
        aviso.Pendiente.Should().BeTrue();
        aviso.Eliminado.Should().BeFalse();
        aviso.TenantId.Should().Be(Cliente);
    }

    [PostgresFact]
    public async Task AC1_UnCambioEnUnaTablaHijaActualizaElAvisoConLaVersionNueva()
    {
        await SembrarAsync();
        await MarcarEncoladoAsync(Tramite);

        await EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, 'entregado')");

        var aviso = await AvisoAsync(Tramite);
        aviso!.SyncVersion.Should().Be(await EscalarAsync<long>("SELECT sync_version FROM tramites.procedure_instances WHERE id = @id"));
        aviso.Pendiente.Should().BeTrue("el cambio de una hija vuelve a dejar el aviso pendiente");
    }

    [PostgresFact]
    public async Task AC1_SiLaTransaccionHaceRollbackNoQuedaAviso()
    {
        await SembrarAsync();
        await EjecutarAsync("DELETE FROM integrations.flito_sync_signals");

        await using (var conn = await Fixture.OpenConnectionAsync())
        await using (var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await using var cmd = Comando("UPDATE tramites.procedure_instances SET status = status WHERE id = @id", conn, tx);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }

        (await AvisoAsync(Tramite)).Should().BeNull();
    }

    [PostgresFact]
    public async Task AC2_VariosCambiosAntesDelDespachoDejanUnaSolaFilaConLaVersionMasReciente()
    {
        await SembrarAsync();
        var pendienteDesde = (await AvisoAsync(Tramite))!.PendienteDesde;

        for (var i = 0; i < 3; i++)
        {
            await EjecutarAsync("UPDATE tramites.procedure_instances SET status = status WHERE id = @id");
        }

        (await EscalarAsync<long>("SELECT count(*) FROM integrations.flito_sync_signals WHERE procedure_instance_id = @id")).Should().Be(1);
        var aviso = await AvisoAsync(Tramite);
        aviso!.SyncVersion.Should().Be(await EscalarAsync<long>("SELECT sync_version FROM tramites.procedure_instances WHERE id = @id"));
        aviso.PendienteDesde.Should().Be(pendienteDesde, "un aviso que sigue pendiente conserva desde cuándo lo está");
    }

    [PostgresFact]
    public async Task AC2_UnCambioDespuesDelDespachoVuelveADejarElAvisoPendiente()
    {
        await SembrarAsync();
        await MarcarEncoladoAsync(Tramite);
        (await AvisoAsync(Tramite))!.Pendiente.Should().BeFalse();

        await EjecutarAsync("UPDATE tramites.procedure_instances SET status = status WHERE id = @id");

        var aviso = await AvisoAsync(Tramite);
        aviso!.Pendiente.Should().BeTrue();
        aviso.EncoladoEn.Should().NotBeNull("se conserva el último despacho");
    }

    [PostgresFact]
    public async Task AC3_ElBorradoLogicoDejaElAvisoComoTombstone()
    {
        await SembrarAsync();
        await MarcarEncoladoAsync(Tramite);

        await EjecutarAsync("UPDATE tramites.procedure_instances SET deleted_at = now() WHERE id = @id");

        var aviso = await AvisoAsync(Tramite);
        aviso!.Eliminado.Should().BeTrue();
        aviso.Pendiente.Should().BeTrue();
    }

    [PostgresFact]
    public async Task AC3_ElBorradoEnLaMismaTransaccionDeOtroCambioTambienQuedaEnElAviso()
    {
        await SembrarAsync();

        await using (var conn = await Fixture.OpenConnectionAsync())
        await using (var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            foreach (var sql in new[]
            {
                "UPDATE tramites.procedure_instances SET status = status WHERE id = @id",
                "UPDATE tramites.procedure_instances SET deleted_at = now() WHERE id = @id",
            })
            {
                await using var cmd = Comando(sql, conn, tx);
                await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }

        (await AvisoAsync(Tramite))!.Eliminado.Should().BeTrue("el sello ya estaba puesto, pero el borrado no puede quedar fuera del aviso");
    }

    [PostgresFact]
    public async Task AC4_UnTramiteMigradoNoGeneraAviso()
    {
        await SembrarAsync();
        await SembrarTramiteAsync(Migrado, 2, migrado: true);

        await EjecutarAsync("UPDATE tramites.procedure_instances SET status = status WHERE id = @migrado");
        await EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @migrado, 'entregado')");

        (await AvisoAsync(Migrado)).Should().BeNull();
    }

    [PostgresFact]
    public async Task AC4_MarcarComoMigradoRetiraElAvisoPendiente()
    {
        await SembrarAsync();
        (await AvisoAsync(Tramite)).Should().NotBeNull();

        await EjecutarAsync("UPDATE tramites.procedure_instances SET is_migrated = true WHERE id = @id");

        (await AvisoAsync(Tramite)).Should().BeNull();
    }

    [PostgresFact]
    public async Task AC5_ElRegistroDelAvisoNoSubeRowVersionNiAuditaElTramite()
    {
        await SembrarAsync();
        var rowVersionAntes = await EscalarAsync<long>("SELECT row_version FROM tramites.procedure_instances WHERE id = @id");
        var auditoriaAntes = await AuditoriaAsync();

        await EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, 'entregado')");

        (await AvisoAsync(Tramite))!.Pendiente.Should().BeTrue();
        (await EscalarAsync<long>("SELECT row_version FROM tramites.procedure_instances WHERE id = @id")).Should().Be(rowVersionAntes);
        (await AuditoriaAsync()).Should().Be(auditoriaAntes);
    }

    [PostgresFact]
    public async Task AC5_DosEscriturasEnHijasDelMismoTramiteNoSeBloqueanPorElAviso()
    {
        await SembrarAsync();

        // Una transacción escribe en una hija (FOR KEY SHARE sobre el trámite + sello + aviso) y no confirma;
        // otra escribe en otra hija del mismo trámite. Sin FK desde el aviso al trámite no hay cruce: la segunda
        // solo espera el bloqueo de fila del trámite, y termina en cuanto la primera confirma.
        await using var conn1 = await Fixture.OpenConnectionAsync();
        await using var tx1 = await conn1.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var cmd = Comando(
            "INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text) VALUES (@tenant, @id, 'vehicle_brand', 'MARCA EJEMPLO')",
            conn1, tx1))
        {
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var segunda = Task.Run(async () =>
        {
            await using var conn2 = await Fixture.OpenConnectionAsync();
            await using var tx2 = await conn2.BeginTransactionAsync();
            await using (var t = new NpgsqlCommand("SET LOCAL deadlock_timeout = '100ms'", conn2, tx2))
            {
                await t.ExecuteNonQueryAsync();
            }

            await using (var cmd = Comando(
                "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, 'entregado')",
                conn2, tx2))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            await tx2.CommitAsync();
        });

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await tx1.CommitAsync(TestContext.Current.CancellationToken);
        await FluentActions.Awaiting(() => segunda).Should().NotThrowAsync("no hay deadlock 40P01");

        (await EscalarAsync<long>("SELECT count(*) FROM integrations.flito_sync_signals WHERE procedure_instance_id = @id")).Should().Be(1);
        (await AvisoAsync(Tramite))!.SyncVersion.Should().Be(
            await EscalarAsync<long>("SELECT sync_version FROM tramites.procedure_instances WHERE id = @id"));
    }

    private sealed record Aviso(Guid TenantId, long SyncVersion, bool Eliminado, DateTimeOffset? PendienteDesde, DateTimeOffset? EncoladoEn)
    {
        public bool Pendiente => PendienteDesde is not null;
    }

    private async Task<Aviso?> AvisoAsync(Guid id)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT tenant_id, sync_version, eliminado, pendiente_desde, encolado_en FROM integrations.flito_sync_signals WHERE procedure_instance_id = @x",
            conn);
        cmd.Parameters.AddWithValue("x", id);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        if (!await r.ReadAsync(TestContext.Current.CancellationToken))
        {
            return null;
        }

        return new Aviso(
            r.GetGuid(0),
            r.GetInt64(1),
            r.GetBoolean(2),
            r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3),
            r.IsDBNull(4) ? null : r.GetFieldValue<DateTimeOffset>(4));
    }

    /// <summary>Simula el despacho (HU #13624): el aviso deja de estar pendiente.</summary>
    private Task MarcarEncoladoAsync(Guid id) =>
        EjecutarAsync(
            $"UPDATE integrations.flito_sync_signals SET pendiente_desde = NULL, encolado_en = now() WHERE procedure_instance_id = '{id}'");

    private Task<long> AuditoriaAsync() =>
        EscalarAsync<long>(
            "SELECT count(*) FROM audit.audit_logs WHERE schema_name = 'tramites' AND table_name = 'procedure_instances' AND record_id = @id");

    private async Task SembrarAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Cliente, "IT-13622", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync();
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13622@flit.test",
                DisplayName = "Gestor 13622",
                Status = "active",
                HomeTenantId = Cliente,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
            await ctx.SaveChangesAsync();
        }

        await SembrarTramiteAsync(Tramite, 1, migrado: false);
    }

    private async Task SembrarTramiteAsync(Guid id, int n, bool migrado)
    {
        await using var ctx = NewContext();
        var tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();

        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = Cliente,
            ProcedureTypeId = tipo,
            ReferenceNumber = $"IT13622-{n}",
            Status = TramiteEstado.Borrador,
            Vin = $"VIN13622{n:D9}",
            CreatedByUserId = Gestor,
            CreatedAt = DateTimeOffset.UtcNow,
            IsMigrated = migrado,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task EjecutarAsync(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = Comando(sql, conn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<T> EscalarAsync<T>(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = Comando(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static NpgsqlCommand Comando(string sql, NpgsqlConnection conn, NpgsqlTransaction? tx = null)
    {
        var cmd = new NpgsqlCommand(sql, conn, tx);
        if (sql.Contains("@id", StringComparison.Ordinal)) cmd.Parameters.AddWithValue("id", Tramite);
        if (sql.Contains("@migrado", StringComparison.Ordinal)) cmd.Parameters.AddWithValue("migrado", Migrado);
        if (sql.Contains("@tenant", StringComparison.Ordinal)) cmd.Parameters.AddWithValue("tenant", Cliente);
        return cmd;
    }
}

using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13074 (Feature #13062, Épica #12737) contra Postgres real con TODAS las migraciones: los
/// cambios en las cinco tablas hijas mueven la versión del trámite (AC1), una vez por transacción
/// (AC2, AC4), sin subir <c>row_version</c> ni auditar el trámite por ello, y la asignación inicial
/// del histórico va en orden de creación sin auditoría (AC3).
/// </summary>
public sealed class SyncPropagacionHijasMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Cliente = new("5a5a5a5a-0001-4000-8000-000000013074");
    private static readonly Guid Gestor = new("5a5a5a5a-0002-4000-8000-000000013074");
    private static readonly Guid Tramite = new("5a5a5a5a-0003-4000-8000-000000013074");
    private static readonly Guid Otro = new("5a5a5a5a-0004-4000-8000-000000013074");

    /// <summary>INSERT, UPDATE y DELETE de una fila en cada tabla hija. <c>@id</c> = trámite.</summary>
    public static TheoryData<string, string, string, string> CambiosPorTabla() => new()
    {
        {
            "actores",
            "INSERT INTO tramites.procedure_instance_actors (id, tenant_id, procedure_instance_id, procedure_entity_id, actor_type, document_type, document_number, full_name) "
            + "SELECT '5a5a5a5a-0005-4000-8000-000000000001', @tenant, @id, (SELECT id FROM tramites.procedure_entities ORDER BY id LIMIT 1), 'comprador', 'CC', '1000000000', 'PERSONA EJEMPLO'",
            "UPDATE tramites.procedure_instance_actors SET full_name = 'PERSONA EJEMPLO DOS' WHERE procedure_instance_id = @id",
            "DELETE FROM tramites.procedure_instance_actors WHERE procedure_instance_id = @id"
        },
        {
            "campos",
            "INSERT INTO tramites.procedure_instance_field_values (id, tenant_id, procedure_instance_id, field_key, value_text) VALUES ('5a5a5a5a-0006-4000-8000-000000000001', @tenant, @id, 'vehicle_brand', 'MARCA EJEMPLO')",
            "UPDATE tramites.procedure_instance_field_values SET value_text = 'OTRA MARCA' WHERE procedure_instance_id = @id",
            "DELETE FROM tramites.procedure_instance_field_values WHERE procedure_instance_id = @id"
        },
        {
            "historial",
            "INSERT INTO tramites.procedure_instance_status_history (id, tenant_id, procedure_instance_id, from_status, to_status) VALUES ('5a5a5a5a-0007-4000-8000-000000000001', @tenant, @id, NULL, 'borrador')",
            "UPDATE tramites.procedure_instance_status_history SET reason = 'ajuste' WHERE procedure_instance_id = @id",
            "DELETE FROM tramites.procedure_instance_status_history WHERE procedure_instance_id = @id"
        },
        {
            "adjuntos",
            "INSERT INTO tramites.procedure_instance_attachments (id, tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path) VALUES ('5a5a5a5a-0008-4000-8000-000000000001', @tenant, @id, 'factura', 'factura.pdf', 'application/pdf', 10, repeat('a', 64), 'it/factura.pdf')",
            "UPDATE tramites.procedure_instance_attachments SET filename = 'factura-2.pdf' WHERE procedure_instance_id = @id",
            "DELETE FROM tramites.procedure_instance_attachments WHERE procedure_instance_id = @id"
        },
        {
            "comercial",
            "INSERT INTO tramites.procedure_instance_commercial (id, tenant_id, procedure_instance_id, valor_venta) VALUES ('5a5a5a5a-0009-4000-8000-000000000001', @tenant, @id, 1000)",
            "UPDATE tramites.procedure_instance_commercial SET valor_venta = 2000 WHERE procedure_instance_id = @id",
            "DELETE FROM tramites.procedure_instance_commercial WHERE procedure_instance_id = @id"
        },
    };

    [PostgresTheory]
    [MemberData(nameof(CambiosPorTabla))]
    public async Task AC1_InsertarModificarOBorrarUnaFilaHijaSubeLaVersion(
        string tabla, string insertar, string modificar, string borrar)
    {
        await SembrarAsync(Tramite, 1);

        foreach (var sentencia in new[] { insertar, modificar, borrar })
        {
            var antes = await VersionAsync(Tramite);
            await EjecutarAsync(sentencia);
            (await VersionAsync(Tramite)).Should().BeGreaterThan(antes, $"{tabla}: {sentencia[..20]}");
        }
    }

    [PostgresFact]
    public async Task AC1_ElCambioDeUnaHijaNoMueveAOtrosTramites()
    {
        await SembrarAsync(Tramite, 1);
        await SembrarTramiteAsync(Otro, 2);
        var otroAntes = await VersionAsync(Otro);

        await EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, 'borrador')");

        (await VersionAsync(Otro)).Should().Be(otroAntes);
    }

    [PostgresFact]
    public async Task AC2_UnaSentenciaConVariasFilasHijasSubeLaVersionUnaSolaVez()
    {
        await SembrarAsync(Tramite, 1);
        var secuenciaAntes = await SecuenciaAsync();

        await EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) "
            + "SELECT @tenant, @id, s FROM unnest(ARRAY['borrador','preparado','entregado']) s");

        (await SecuenciaAsync()).Should().Be(secuenciaAntes + 1);
        (await VersionAsync(Tramite)).Should().Be(secuenciaAntes + 1);
    }

    [PostgresFact]
    public async Task AC2_VariasSentenciasEnUnaTransaccionSubenLaVersionUnaSolaVez()
    {
        await SembrarAsync(Tramite, 1);
        var secuenciaAntes = await SecuenciaAsync();

        await EjecutarEnTransaccionAsync(
            "UPDATE tramites.procedure_instances SET prioritario = true WHERE id = @id",
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, 'borrador')",
            "INSERT INTO tramites.procedure_instance_commercial (tenant_id, procedure_instance_id, valor_venta) VALUES (@tenant, @id, 5)");

        (await SecuenciaAsync()).Should().Be(secuenciaAntes + 1);
    }

    [PostgresFact]
    public async Task AC4_LosTriggersDeDenormalizacionNoDuplicanElIncrementoNiFallan()
    {
        await SembrarAsync(Tramite, 1);
        var secuenciaAntes = await SecuenciaAsync();

        // vin y comprador disparan los triggers de denormalización del DDL 47, que actualizan el
        // trámite fila a fila, además del toque de sincronización de la sentencia.
        await EjecutarEnTransaccionAsync(
            "INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text) VALUES (@tenant, @id, 'vin', 'VINEJEMPLO0000001'), (@tenant, @id, 'plate', 'ABC123')",
            "INSERT INTO tramites.procedure_instance_actors (tenant_id, procedure_instance_id, procedure_entity_id, actor_type, document_type, document_number, full_name) "
            + "SELECT @tenant, @id, (SELECT id FROM tramites.procedure_entities ORDER BY id LIMIT 1), 'comprador', 'CC', '1000000000', 'PERSONA EJEMPLO'");

        (await SecuenciaAsync()).Should().Be(secuenciaAntes + 1);
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT vin, plate, comprador_nombre FROM tramites.procedure_instances WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", Tramite);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await r.ReadAsync(TestContext.Current.CancellationToken);
        (r.GetString(0), r.GetString(1), r.GetString(2)).Should().Be(("VINEJEMPLO0000001", "ABC123", "PERSONA EJEMPLO"));
    }

    [PostgresFact]
    public async Task ElToqueNoSubeRowVersionNiAuditaElTramite()
    {
        await SembrarAsync(Tramite, 1);
        var rowVersionAntes = await EscalarAsync<long>("SELECT row_version FROM tramites.procedure_instances WHERE id = @id");
        var auditoriaAntes = await AuditoriaDelTramiteAsync();

        await EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, 'borrador')");

        (await EscalarAsync<long>("SELECT row_version FROM tramites.procedure_instances WHERE id = @id")).Should().Be(rowVersionAntes);
        (await AuditoriaDelTramiteAsync()).Should().Be(auditoriaAntes);

        // Un cambio real del trámite sigue subiendo row_version y quedando auditado.
        await EjecutarAsync("UPDATE tramites.procedure_instances SET prioritario = true WHERE id = @id");
        (await EscalarAsync<long>("SELECT row_version FROM tramites.procedure_instances WHERE id = @id")).Should().Be(rowVersionAntes + 1);
        (await AuditoriaDelTramiteAsync()).Should().Be(auditoriaAntes + 1);
    }

    [PostgresFact]
    public async Task EfGuardaElTramiteYSuHistorialJuntosSinErrorDeConcurrencia()
    {
        await SembrarAsync(Tramite, 1);
        var antes = await VersionAsync(Tramite);

        await using (var ctx = NewContext())
        {
            var tramite = await ctx.ProcedureInstances.SingleAsync(p => p.Id == Tramite);

            // Otro proceso agrega una fila hija mientras este contexto tiene el trámite cargado.
            await EjecutarAsync(
                "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, 'borrador')");

            tramite.Status = TramiteEstado.Preparado;
            ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
            {
                Id = Guid.CreateVersion7(),
                TenantId = Cliente,
                ProcedureInstanceId = Tramite,
                FromStatus = TramiteEstado.Borrador,
                ToStatus = TramiteEstado.Preparado,
                ChangedAt = DateTimeOffset.UtcNow,
            });

            var guardar = async () => await ctx.SaveChangesAsync();
            await guardar.Should().NotThrowAsync<DbUpdateConcurrencyException>();
        }

        (await VersionAsync(Tramite)).Should().BeGreaterThan(antes);
    }

    [PostgresFact]
    public async Task AC3_LaAsignacionInicialNumeraElHistoricoEnOrdenDeCreacionSinAuditoria()
    {
        await SembrarAsync(Tramite, 1);
        await SembrarTramiteAsync(Otro, 2);

        // Se simula el histórico previo a la migración: dos trámites sin versión, el «Otro» más antiguo.
        await EjecutarSinTriggersAsync(
            "UPDATE tramites.procedure_instances SET sync_version = 0, created_at = now() - interval '2 days' WHERE id = @otro",
            "UPDATE tramites.procedure_instances SET sync_version = 0, created_at = now() - interval '1 day' WHERE id = @id");
        var maximoAntes = await SecuenciaAsync();
        var auditoriaAntes = await AuditoriaDelTramiteAsync();
        var rowVersionAntes = await EscalarAsync<long>("SELECT row_version FROM tramites.procedure_instances WHERE id = @id");

        await EjecutarUpAsync();

        var otro = await VersionAsync(Otro);
        var tramite = await VersionAsync(Tramite);
        otro.Should().Be(maximoAntes + 1, "el más antiguo recibe la primera versión del bloque reservado");
        tramite.Should().Be(maximoAntes + 2);
        (await SecuenciaAsync()).Should().Be(maximoAntes + 2, "la secuencia queda al final del bloque");
        (await AuditoriaDelTramiteAsync()).Should().Be(auditoriaAntes);
        (await EscalarAsync<long>("SELECT row_version FROM tramites.procedure_instances WHERE id = @id")).Should().Be(rowVersionAntes);
    }

    [PostgresFact]
    public async Task ReaplicarElUp_NoProduceErrorNiRenumera()
    {
        await SembrarAsync(Tramite, 1);
        var antes = await VersionAsync(Tramite);

        await EjecutarUpAsync();
        await EjecutarUpAsync();

        (await VersionAsync(Tramite)).Should().Be(antes);
    }

    // ── Siembra y utilidades ────────────────────────────────────────────────

    private async Task SembrarAsync(Guid tramite, int n)
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Cliente, "IT-13074", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync();
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13074@flit.test",
                DisplayName = "Gestor 13074",
                Status = "active",
                HomeTenantId = Cliente,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
            await ctx.SaveChangesAsync();
        }

        await SembrarTramiteAsync(tramite, n);
    }

    private async Task SembrarTramiteAsync(Guid id, int n)
    {
        await using var ctx = NewContext();
        var tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();

        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = Cliente,
            ProcedureTypeId = tipo,
            ReferenceNumber = $"IT13074-{n}",
            Status = TramiteEstado.Borrador,
            Vin = $"VIN13074{n:D9}",
            CreatedByUserId = Gestor,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private static NpgsqlCommand Comando(string sql, NpgsqlConnection conn, NpgsqlTransaction? tx = null)
    {
        var cmd = new NpgsqlCommand(sql, conn, tx);
        if (sql.Contains("@id", StringComparison.Ordinal)) cmd.Parameters.AddWithValue("id", Tramite);
        if (sql.Contains("@otro", StringComparison.Ordinal)) cmd.Parameters.AddWithValue("otro", Otro);
        if (sql.Contains("@tenant", StringComparison.Ordinal)) cmd.Parameters.AddWithValue("tenant", Cliente);
        return cmd;
    }

    private async Task EjecutarAsync(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = Comando(sql, conn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task EjecutarEnTransaccionAsync(params string[] sentencias)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        foreach (var sql in sentencias)
        {
            await using var cmd = Comando(sql, conn, tx);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        await tx.CommitAsync(TestContext.Current.CancellationToken);
    }

    private async Task EjecutarSinTriggersAsync(params string[] sentencias) =>
        await EjecutarEnTransaccionAsync(
            ["ALTER TABLE tramites.procedure_instances DISABLE TRIGGER USER",
             .. sentencias,
             "ALTER TABLE tramites.procedure_instances ENABLE TRIGGER USER"]);

    private async Task EjecutarUpAsync()
    {
        var up = new HU13074_SyncPropagacionHijas().UpOperations.OfType<SqlOperation>().Single().Sql;
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(up, conn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<T> EscalarAsync<T>(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = Comando(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<long> VersionAsync(Guid id)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT sync_version FROM tramites.procedure_instances WHERE id = @x", conn);
        cmd.Parameters.AddWithValue("x", id);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private Task<long> SecuenciaAsync() =>
        EscalarAsync<long>("SELECT last_value FROM tramites.procedure_sync_seq");

    private Task<long> AuditoriaDelTramiteAsync() =>
        EscalarAsync<long>(
            "SELECT count(*) FROM audit.audit_logs WHERE schema_name = 'tramites' AND table_name = 'procedure_instances' AND record_id = @id");
}

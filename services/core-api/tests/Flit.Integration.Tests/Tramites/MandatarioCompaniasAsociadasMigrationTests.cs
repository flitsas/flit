using Flit.Infrastructure.Persistence;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13177 (Feature #13119 F7, Épica #13090) — migración 126: tabla
/// <c>admin.mandate_signer_associated_companies</c>, contra PostgreSQL real (esquema, unicidad parcial,
/// idempotencia y reversa).
/// </summary>
public sealed class MandatarioCompaniasAsociadasMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string MigrationId = "20260930190000_HU13177_MandatarioCompaniasAsociadas";
    private const string Table = "mandate_signer_associated_companies";

    private static readonly Guid Ot1 = new("0199d000-0000-7000-8000-000000000001");
    private static readonly Guid Tenant1 = new("b1000000-0000-7000-8000-0000000000d1");
    private static readonly Guid Tenant2 = new("b2000000-0000-7000-8000-0000000000d2");
    private static readonly Guid Signer1 = new("0199d000-0000-7000-8000-000000000101");

    private bool _downgraded;

    public override async ValueTask DisposeAsync()
    {
        if (_downgraded && PostgresAvailability.IsAvailable)
        {
            await MigrateAsync(null);
            _downgraded = false;
        }

        await base.DisposeAsync();
    }

    private async Task MigrateAsync(string? target)
    {
        await using var ctx = NewContext();
        await ctx.GetService<IMigrator>().MigrateAsync(target, TestContext.Current.CancellationToken);
    }

    private async Task MigrateToPreviousAsync()
    {
        await using var ctx = NewContext();
        var all = ctx.Database.GetMigrations().ToList();
        var idx = all.IndexOf(MigrationId);
        idx.Should().BeGreaterThan(0, "la migración HU13177 debe estar descubierta por EF (atributos inline)");
        _downgraded = true;
        await ctx.GetService<IMigrator>().MigrateAsync(all[idx - 1], TestContext.Current.CancellationToken);
    }

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static Task InsertAsync(NpgsqlConnection cn, Guid tenant, bool active = true) =>
        ExecAsync(cn,
            "INSERT INTO admin.mandate_signer_associated_companies " +
            "(mandate_signer_id, transit_office_id, associated_company_tenant_id, is_active) VALUES (@s, @o, @t, @a)",
            ("s", Signer1), ("o", Ot1), ("t", tenant), ("a", active));

    private async Task SeedAsync(NpgsqlConnection cn)
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Tenant1, "IT-CIA-D1", false, null));
            ctx.Tenants.Add(TenantSeed.New(Tenant2, "IT-CIA-D2", false, null));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await ExecAsync(cn,
            "INSERT INTO admin.mandate_signers (id, transit_office_id, full_name, document_type, document_number, " +
            "integrity_hash, registered_at, created_at, signer_model, signature_method) " +
            "VALUES (@id, @o, 'Mandatario de prueba', 'CC', '770001', 'h', now(), now(), 'natural', 'baul')",
            ("id", Signer1), ("o", Ot1));
    }

    [PostgresFact]
    public async Task AC1_LaTablaExiste_ConColumnasFkIndicesYUnicoParcial()
    {
        await using var cn = await Fixture.OpenConnectionAsync();

        (await CountAsync(cn,
            "SELECT count(*) FROM information_schema.columns WHERE table_schema='admin' AND table_name='" + Table + "' " +
            "AND column_name IN ('id','mandate_signer_id','transit_office_id','associated_company_tenant_id','is_active','created_at')"))
            .Should().Be(6);

        (await CountAsync(cn,
            "SELECT count(*) FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid " +
            "WHERE t.relname='" + Table + "' AND c.contype='f'")).Should().Be(2);
        (await CountAsync(cn,
            "SELECT count(*) FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid " +
            "WHERE t.relname='" + Table + "' AND c.conname='fk_msac_mandate_signer' AND c.confdeltype='c'")).Should().Be(1);

        (await CountAsync(cn,
            "SELECT count(*) FROM pg_indexes WHERE schemaname='admin' AND tablename='" + Table + "' " +
            "AND indexname IN ('uq_msac_activa','ix_msac_office_company','ix_msac_signer','ix_msac_company_tenant')"))
            .Should().Be(4);
        (await CountAsync(cn,
            "SELECT count(*) FROM pg_indexes WHERE schemaname='admin' AND tablename='" + Table + "' " +
            "AND indexname='uq_msac_activa' AND indexdef ILIKE '%UNIQUE%' AND indexdef ILIKE '%WHERE%is_active%'"))
            .Should().Be(1);
    }

    [PostgresFact]
    public async Task AC2_NoAdmiteDuplicadosActivos_PeroSiElAnteriorEstaInactivo()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);

        await InsertAsync(cn, Tenant1);
        var dup = async () => await InsertAsync(cn, Tenant1);
        (await dup.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");

        await ExecAsync(cn, "UPDATE admin.mandate_signer_associated_companies SET is_active = false");
        await InsertAsync(cn, Tenant1); // baja lógica: la unicidad solo cuenta las activas
        await InsertAsync(cn, Tenant2);

        (await CountAsync(cn, "SELECT count(*) FROM admin.mandate_signer_associated_companies")).Should().Be(3);
    }

    [PostgresFact]
    public async Task AC2_LaFkAlTenantRechazaUnTenantInexistente_YElBorradoDelMandatarioCascadea()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);

        var huerfano = async () => await InsertAsync(cn, Guid.NewGuid());
        (await huerfano.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23503");

        await InsertAsync(cn, Tenant1);
        await ExecAsync(cn, "DELETE FROM admin.mandate_signers WHERE id = @id", ("id", Signer1));
        (await CountAsync(cn, "SELECT count(*) FROM admin.mandate_signer_associated_companies")).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC3_EsIdempotente_YLaReversaQuitaLaTablaSinDejarObjetos()
    {
        await using var cn = await Fixture.OpenConnectionAsync();

        // Segunda aplicación del DDL sobre lo ya creado: no falla.
        await ExecAsync(cn, File.ReadAllText(FindDdl()));

        await MigrateToPreviousAsync();
        (await CountAsync(cn,
            "SELECT count(*) FROM information_schema.tables WHERE table_schema='admin' AND table_name='" + Table + "'"))
            .Should().Be(0);
        (await CountAsync(cn,
            "SELECT count(*) FROM pg_indexes WHERE schemaname='admin' AND indexname LIKE '%msac%'")).Should().Be(0);

        await MigrateAsync(null);
        _downgraded = false;
        (await CountAsync(cn,
            "SELECT count(*) FROM information_schema.tables WHERE table_schema='admin' AND table_name='" + Table + "'"))
            .Should().Be(1);
    }

    private static string FindDdl()
    {
        const string name = "126-HU13177-mandatario-companias-asociadas.sql";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Flit.Infrastructure", "Persistence", "Sql", "Ddl", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(name);
    }
}

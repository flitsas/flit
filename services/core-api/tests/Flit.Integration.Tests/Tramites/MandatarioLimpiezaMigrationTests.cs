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
/// HU #13160 (Feature #13120, Épica #13090) — migración 127 de limpieza (DESTRUCTIVA), contra la base EFÍMERA del
/// arnés (nunca contra la base local compartida): elimina <c>admin.admin_identity_validations</c>,
/// <c>admin.mandate_signers.identity_validation_ref</c> y <c>admin.transit_office_mandate_config.custom_field_manifest</c>,
/// conserva la columna homónima del representante legal, aborta si hay datos y su reversa recrea la estructura.
/// <para>Uso de ejemplo: <c>MigrateAsync("20260930200000_HU13148_...")</c> deja el esquema previo (Down) y
/// <c>MigrateAsync(null)</c> aplica la limpieza.</para>
/// </summary>
public sealed class MandatarioLimpiezaMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string MigrationId = "20261001140334_HU13160_LimpiezaIdentidadMandatario";

    private bool _downgraded;

    public override async ValueTask DisposeAsync()
    {
        if (_downgraded && PostgresAvailability.IsAvailable)
        {
            await using var cn = await Fixture.OpenConnectionAsync();
            await Exec(cn, "DELETE FROM admin.admin_identity_validations");
            await MigrateAsync(null);
        }

        await base.DisposeAsync();
    }

    [PostgresFact]
    public async Task AC1_AC2_AC4_LaLimpiezaAplica_ConservaLaColumnaDelRepresentanteLegal_YQuedaRegistrada()
    {
        await using var cn = await Fixture.OpenConnectionAsync();

        (await TableExists(cn, "admin_identity_validations")).Should().BeFalse();
        (await Columns(cn, "mandate_signers")).Should().NotContain("identity_validation_ref");
        (await Columns(cn, "transit_office_mandate_config")).Should().NotContain("custom_field_manifest");
        (await Columns(cn, "company_legal_representatives")).Should().Contain(
            "identity_validation_ref", "el representante legal la usa activamente");
        (await Scalar(cn, "SELECT count(*) FROM public.\"__EFMigrationsHistory\" WHERE migration_id = @m", ("m", MigrationId)))
            .Should().Be(1, "EF descubre la migración por sus atributos inline y la registra una sola vez");
    }

    [PostgresFact]
    public async Task Reversa_RecreaLaEstructuraSinDatos_YSeReaplica()
    {
        await MigrateAsync(await PreviousMigrationAsync());
        await using var cn = await Fixture.OpenConnectionAsync();

        (await TableExists(cn, "admin_identity_validations")).Should().BeTrue();
        (await Columns(cn, "admin_identity_validations")).Should().Contain(["attempts", "max_attempts", "last_attempt_at"]);
        (await Scalar(cn, "SELECT count(*) FROM pg_indexes WHERE schemaname='admin' AND tablename='admin_identity_validations'"))
            .Should().Be(4, "clave primaria y 3 índices");
        (await Scalar(cn,
            "SELECT count(*) FROM pg_trigger WHERE tgrelid='admin.admin_identity_validations'::regclass AND NOT tgisinternal"))
            .Should().Be(2, "row_version y auditoría");
        (await Scalar(cn, "SELECT count(*) FROM admin.admin_identity_validations")).Should().Be(0);
        (await Columns(cn, "mandate_signers")).Should().Contain("identity_validation_ref");
        (await Columns(cn, "transit_office_mandate_config")).Should().Contain("custom_field_manifest");

        await MigrateAsync(null);
        _downgraded = false;
        (await TableExists(cn, "admin_identity_validations")).Should().BeFalse();
    }

    [PostgresFact]
    public async Task AC5_ConDatosEnUso_LaMigracionAbortaSinBorrarNada()
    {
        await MigrateAsync(await PreviousMigrationAsync());
        await using var cn = await Fixture.OpenConnectionAsync();
        var tenant = Guid.CreateVersion7();
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(tenant, "IT-LIMP", false, null));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await Exec(cn,
            "INSERT INTO admin.admin_identity_validations (tenant_id, subject_type, subject_ref, document_type, document_number, name, email) " +
            "VALUES (@t, 'mandate_signer', @s, 'CC', '1', 'Persona', 'p@x.co')",
            ("t", tenant), ("s", Guid.CreateVersion7()));

        var act = async () => await MigrateAsync(null);

        (await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("HU #13160");
        (await TableExists(cn, "admin_identity_validations")).Should().BeTrue("no se borra nada si hay datos");
        (await Scalar(cn, "SELECT count(*) FROM admin.admin_identity_validations")).Should().Be(1);
        (await Columns(cn, "mandate_signers")).Should().Contain("identity_validation_ref");
    }

    // ── infraestructura ────────────────────────────────────────────────────────────────────────────

    private async Task<string> PreviousMigrationAsync()
    {
        await using var ctx = NewContext();
        var all = ctx.Database.GetMigrations().ToList();
        var idx = all.IndexOf(MigrationId);
        idx.Should().BeGreaterThan(0, "la migración HU13160 debe estar descubierta por EF (Designer.cs)");
        return all[idx - 1];
    }

    private async Task MigrateAsync(string? target)
    {
        if (target is not null)
        {
            _downgraded = true;
        }

        await using var ctx = NewContext();
        await ctx.GetService<IMigrator>().MigrateAsync(target, TestContext.Current.CancellationToken);
    }

    private static async Task Exec(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> Scalar(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<bool> TableExists(NpgsqlConnection cn, string table) =>
        await Scalar(cn,
            "SELECT count(*) FROM information_schema.tables WHERE table_schema='admin' AND table_name=@t", ("t", table)) > 0;

    private static async Task<List<string>> Columns(NpgsqlConnection cn, string table)
    {
        var cols = new List<string>();
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema='admin' AND table_name=@t", cn);
        cmd.Parameters.AddWithValue("t", table);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await r.ReadAsync(TestContext.Current.CancellationToken))
        {
            cols.Add(r.GetString(0));
        }

        return cols;
    }
}

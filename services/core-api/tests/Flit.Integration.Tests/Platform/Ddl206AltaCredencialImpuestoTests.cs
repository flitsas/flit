using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// HU #14711 (plan §3.1 punto 5) contra PostgreSQL real — el DDL 206 amplía <c>ck_tenant_product_credentials_code</c> a los dos
/// secretos del webhook de <c>diagnostico</c> y suma a <c>platform.tenant_product_provisioning</c> los intentos y el próximo
/// intento del alta automática. Idempotente; el Down borra solo las filas de los secretos y restaura el CHECK.
/// </summary>
/// <remarks>
/// Uso de ejemplo: insertar <c>('diagnostico','impuesto_webhook_secret')</c> → se acepta; <c>('diagnostico','otro')</c> → 23514.
/// </remarks>
public sealed class Ddl206AltaCredencialImpuestoTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "206-alta-credencial-impuesto-diagnostico.sql";
    private const string MigrationSuffix = "_HU14711_AltaCredencialImpuestoDiagnostico";
    private const string Tenant = "14711000-0000-4000-8000-000000000001";
    private const string Cifrado = "cifrado-de-prueba-0123456789";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand(sql, cn, tx);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<long> CountAsync(NpgsqlConnection cn, string sql, NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand(sql, cn, tx);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(Ct));
    }

    private static Task SeedAsync(NpgsqlConnection cn) => ExecAsync(cn, $"""
        INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at) VALUES
          ('{Tenant}','IT-14711-A','Compañía 14711','914711000000001','RENTING',true,false,now());
        """);

    private static string Credencial(string codigo) => $"""
        INSERT INTO platform.tenant_product_credentials (tenant_id, product_code, credential_code, secret_protected)
        VALUES ('{Tenant}', 'diagnostico', '{codigo}', '{Cifrado}');
        """;

    [PostgresFact]
    public async Task Migracion_SeDescubreYEstaAplicada()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal));
        (await ctx.Database.GetAppliedMigrationsAsync(Ct)).Should().Contain(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task Check_AceptaLosSecretosDelWebhookDeDiagnostico_YSigueRechazandoOtrosCodigos()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);

        await ExecAsync(cn, Credencial("impuesto_api_key") + Credencial("impuesto_webhook_secret") + Credencial("impuesto_webhook_secret_anterior"));

        (await CountAsync(cn, $"SELECT count(*) FROM platform.tenant_product_credentials WHERE tenant_id = '{Tenant}'")).Should().Be(3);
        var act = () => ExecAsync(cn, Credencial("otra_llave"));
        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_tenant_product_credentials_code");
    }

    [PostgresFact]
    public async Task Aprovisionamiento_TieneIntentosYProximoIntento_ConIntentosNoNegativos()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);

        await ExecAsync(cn, $"""
            INSERT INTO platform.tenant_product_provisioning (tenant_id, product_code, status, reported_version, reported_at)
            VALUES ('{Tenant}', 'diagnostico', 'PENDIENTE', 1, now());
            """);

        (await CountAsync(cn, $"""
            SELECT count(*) FROM platform.tenant_product_provisioning
             WHERE tenant_id = '{Tenant}' AND attempts = 0 AND next_attempt_at IS NULL
            """)).Should().Be(1);
        var act = () => ExecAsync(cn, $"UPDATE platform.tenant_product_provisioning SET attempts = -1 WHERE tenant_id = '{Tenant}'");
        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_tenant_product_provisioning_attempts");
    }

    [PostgresFact]
    public async Task Ddl_EsIdempotente()
    {
        await using var cn = await Fixture.OpenConnectionAsync();

        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        (await CountAsync(cn, """
            SELECT count(*) FROM pg_constraint WHERE conname IN ('ck_tenant_product_credentials_code', 'ck_tenant_product_provisioning_attempts')
            """)).Should().Be(2);
        (await CountAsync(cn, "SELECT count(*) FROM pg_indexes WHERE indexname = 'ix_tenant_product_provisioning_product_code_next_attempt_at'"))
            .Should().Be(1);
    }

    [PostgresFact]
    public async Task Down_BorraSoloLosSecretosDelWebhook_YRestauraElCheckYLasColumnas()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await ExecAsync(cn, Credencial("impuesto_api_key") + Credencial("impuesto_webhook_secret") + Credencial("impuesto_webhook_secret_anterior"));
        await using var tx = await cn.BeginTransactionAsync(Ct);

        await ExecAsync(cn, HU14711_AltaCredencialImpuestoDiagnostico.DownSql, tx);

        (await CountAsync(cn, $"SELECT count(*) FROM platform.tenant_product_credentials WHERE tenant_id = '{Tenant}' AND credential_code = 'impuesto_api_key'", tx))
            .Should().Be(1, "la llave no es del 206: se conserva");
        (await CountAsync(cn, $"SELECT count(*) FROM platform.tenant_product_credentials WHERE tenant_id = '{Tenant}'", tx)).Should().Be(1);
        (await CountAsync(cn, """
            SELECT count(*) FROM information_schema.columns
             WHERE table_schema = 'platform' AND table_name = 'tenant_product_provisioning' AND column_name IN ('attempts', 'next_attempt_at')
            """, tx)).Should().Be(0);
        var act = () => ExecAsync(cn, Credencial("impuesto_webhook_secret"), tx);
        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_tenant_product_credentials_code");
        await tx.RollbackAsync(Ct);
    }
}

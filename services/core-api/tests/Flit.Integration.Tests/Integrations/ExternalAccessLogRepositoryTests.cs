using System.Net;
using Flit.Admin.Domain.Integrations;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Integrations;

/// <summary>
/// HU #13086 (Feature #13067, Épica #12737) contra Postgres real con TODAS las migraciones: la bitácora de
/// accesos externos guarda cada solicitud con sus tipos reales (inet, uuid[]) y rechaza filas incoherentes.
/// </summary>
public sealed class ExternalAccessLogRepositoryTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    [PostgresFact]
    public async Task AC1_UnaPaginaDelFeedSeGuardaConTodosSusDatos()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var cuando = new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero);

        await new ExternalAccessLogRepository(NewContext()).AddAsync(new ExternalAccessLogEntry(
            "flito-it", "tramites.sync", "req-13086", IPAddress.Parse("203.0.113.7"), 7, 9, 3, [a, b], true, 200, 42, cuando),
            TestContext.Current.CancellationToken);

        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT client_id, endpoint, request_id, host(ip), sync_version_from, sync_version_to, items_count, tenant_ids, "
            + "pii_unmasked, http_status, duration_ms, occurred_at FROM integrations.external_access_log WHERE request_id = 'req-13086'",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.GetString(0).Should().Be("flito-it");
        reader.GetString(1).Should().Be("tramites.sync");
        reader.GetString(3).Should().Be("203.0.113.7");
        reader.GetInt64(4).Should().Be(7);
        reader.GetInt64(5).Should().Be(9);
        reader.GetInt32(6).Should().Be(3);
        reader.GetFieldValue<Guid[]>(7).Should().Equal(a, b);
        reader.GetBoolean(8).Should().BeTrue();
        reader.GetInt32(9).Should().Be(200);
        reader.GetInt32(10).Should().Be(42);
        reader.GetFieldValue<DateTimeOffset>(11).Should().Be(cuando);
    }

    [PostgresFact]
    public async Task AC2_UnRechazoSinPaseSeGuardaConLosCamposOpcionalesEnNull()
    {
        await new ExternalAccessLogRepository(NewContext()).AddAsync(new ExternalAccessLogEntry(
            null, "tramites.sync", "req-13086-401", null, null, null, null, null, false, 401, 3, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM integrations.external_access_log WHERE request_id = 'req-13086-401' AND client_id IS NULL "
            + "AND ip IS NULL AND items_count IS NULL AND tenant_ids IS NULL AND http_status = 401",
            conn);
        ((long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).Should().Be(1);
    }

    [PostgresFact]
    public async Task LasFilasIncoherentesSeRechazan()
    {
        var repo = new ExternalAccessLogRepository(NewContext());

        await FluentActions.Awaiting(() => repo.AddAsync(new ExternalAccessLogEntry(
                "flito-it", "tramites.sync", null, null, null, null, null, null, false, 42, 1, DateTimeOffset.UtcNow),
                TestContext.Current.CancellationToken))
            .Should().ThrowAsync<PostgresException>().Where(e => e.ConstraintName == "ck_external_access_log_http_status");
        await FluentActions.Awaiting(() => new ExternalAccessLogRepository(NewContext()).AddAsync(new ExternalAccessLogEntry(
                "flito-it", "tramites.sync", null, null, 7, null, 1, null, false, 200, 1, DateTimeOffset.UtcNow),
                TestContext.Current.CancellationToken))
            .Should().ThrowAsync<PostgresException>().Where(e => e.ConstraintName == "ck_external_access_log_versions");
    }
}

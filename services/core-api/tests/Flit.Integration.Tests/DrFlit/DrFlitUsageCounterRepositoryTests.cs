using Flit.Infrastructure.DrFlit;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.DrFlit.Application.Abstractions;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.DrFlit;

/// <summary>
/// HU #12919 — contador diario de DR. FLIT contra PostgreSQL real (<c>dr_flit.daily_message_usage</c>,
/// DDL 119). Cubre el incremento, el tope, el aislamiento por tenant, el cambio de día y que dos
/// consumos concurrentes no se pasen del tope.
/// Uso de ejemplo:
/// <code>
/// var counter = new DrFlitUsageCounterRepository(ctx);
/// var r = await counter.TryConsumeAsync(tenantId, userId, dailyLimit: 30, ct); // r.Allowed, r.UsedToday
/// </code>
/// </summary>
public sealed class DrFlitUsageCounterRepositoryTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid TenantA = TenantSeed.LoneId;
    private static readonly Guid TenantB = TenantSeed.ParentId;
    private static readonly Guid UserId = Guid.Parse("0199a000-0000-7000-8000-00000000d001");
    private static readonly DateOnly Day1 = new(2026, 9, 25);
    private static readonly DateOnly Day2 = new(2026, 9, 26);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (!PostgresAvailability.IsAvailable)
            return;

        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        ctx.Tenants.Add(TenantSeed.GroupParent());
        ctx.Users.Add(new User
        {
            Id = UserId,
            Email = "dr.flit.it@example.test",
            DisplayName = "Usuario DR. FLIT",
            Status = "active",
            HomeTenantId = TenantA,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private DrFlitUsageCounterRepository Counter(DateOnly day) => new(NewContext(), () => day);

    // ── AC1 — bajo el tope incrementa la fila del día ───────────────────────────────────

    [PostgresFact]
    public async Task AC1_BajoElTope_IncrementaYReportaLoUsado()
    {
        var ct = TestContext.Current.CancellationToken;

        var first = await Counter(Day1).TryConsumeAsync(TenantA, UserId, 30, ct);
        var second = await Counter(Day1).TryConsumeAsync(TenantA, UserId, 30, ct);

        first.Should().Be(new DrFlitUsageConsumption(true, 1));
        second.Should().Be(new DrFlitUsageConsumption(true, 2));
        (await Counter(Day1).GetUsedTodayAsync(TenantA, UserId, ct)).Should().Be(2);
        (await CountRowsAsync()).Should().Be(1, "un solo registro por (tenant, usuario, día)");
    }

    [PostgresFact]
    public async Task AC1_SinMensajes_UsadoEsCero()
    {
        (await Counter(Day1).GetUsedTodayAsync(TenantA, UserId, TestContext.Current.CancellationToken))
            .Should().Be(0);
    }

    // ── AC2 — tope alcanzado ────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_TopeAlcanzado_NoIncrementaYReportaElTope()
    {
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 3; i++)
            (await Counter(Day1).TryConsumeAsync(TenantA, UserId, 3, ct)).Allowed.Should().BeTrue();

        var blocked = await Counter(Day1).TryConsumeAsync(TenantA, UserId, 3, ct);

        blocked.Allowed.Should().BeFalse();
        blocked.UsedToday.Should().Be(3);
        (await Counter(Day1).GetUsedTodayAsync(TenantA, UserId, ct)).Should().Be(3);
    }

    [PostgresFact]
    public async Task AC2_TopeEnCero_BloqueaSinCrearFila()
    {
        var result = await Counter(Day1).TryConsumeAsync(TenantA, UserId, 0, TestContext.Current.CancellationToken);

        result.Should().Be(new DrFlitUsageConsumption(false, 0));
        (await CountRowsAsync()).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC2_ConsumosConcurrentes_NoSuperanElTope()
    {
        var ct = TestContext.Current.CancellationToken;
        const int limit = 5;

        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => Counter(Day1).TryConsumeAsync(TenantA, UserId, limit, ct), ct)));

        results.Count(r => r.Allowed).Should().Be(limit);
        (await Counter(Day1).GetUsedTodayAsync(TenantA, UserId, ct)).Should().Be(limit);
    }

    // ── AC3 — aislamiento multi-tenant ──────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_ConsumirEnTenantA_NoAfectaTenantB()
    {
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 3; i++)
            await Counter(Day1).TryConsumeAsync(TenantA, UserId, 3, ct);

        (await Counter(Day1).GetUsedTodayAsync(TenantB, UserId, ct)).Should().Be(0);
        var inB = await Counter(Day1).TryConsumeAsync(TenantB, UserId, 3, ct);

        inB.Should().Be(new DrFlitUsageConsumption(true, 1));
        (await Counter(Day1).GetUsedTodayAsync(TenantA, UserId, ct)).Should().Be(3);
    }

    // ── AC4 — cambio de día calendario ──────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC4_DiaNuevo_FilaNuevaSinArrastrarElConteo()
    {
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 3; i++)
            await Counter(Day1).TryConsumeAsync(TenantA, UserId, 3, ct);
        (await Counter(Day1).TryConsumeAsync(TenantA, UserId, 3, ct)).Allowed.Should().BeFalse();

        var nextDay = await Counter(Day2).TryConsumeAsync(TenantA, UserId, 3, ct);

        nextDay.Should().Be(new DrFlitUsageConsumption(true, 1));
        (await CountRowsAsync()).Should().Be(2);
    }

    [PostgresFact]
    public async Task AC4_PorDefecto_UsaElDiaDeBogota()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();

        await new DrFlitUsageCounterRepository(ctx).TryConsumeAsync(TenantA, UserId, 3, ct);

        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT usage_date FROM dr_flit.daily_message_usage", cn);
        var stored = (DateOnly)(await cmd.ExecuteScalarAsync(ct))!;
        var bogotaToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow, TimeZoneInfo.CreateCustomTimeZone("co", TimeSpan.FromHours(-5), "co", "co")).DateTime);
        stored.Should().Be(bogotaToday);
    }

    private async Task<long> CountRowsAsync()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM dr_flit.daily_message_usage", cn);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}

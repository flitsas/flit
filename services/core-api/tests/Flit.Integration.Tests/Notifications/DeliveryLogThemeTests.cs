using Flit.Infrastructure.Notifications.DeliveryLog;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Notifications;

/// <summary>
/// HU #12428 AC5, HU #12430 AC5 — DDL 117 (<c>theme_kind</c>, <c>theme_version</c>,
/// <c>sender_name</c>, <c>sender_email</c> en <c>admin.notification_delivery_logs</c>) contra
/// PostgreSQL real: <see cref="NotificationDeliveryLogWriter"/> persiste los cuatro campos, y el
/// CHECK <c>ck_notification_delivery_logs_theme_kind</c> sigue rechazando cualquier valor fuera de
/// <c>flit</c>/<c>brand</c>/<c>NULL</c> (patrón <c>TenantDomainLifecycleTests</c>).
/// </summary>
public sealed class DeliveryLogThemeTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid TenantId = TenantSeed.LoneId;

    private async Task SeedTenantAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        await ctx.SaveChangesAsync();
    }

    [PostgresFact]
    public async Task AC5_WriteAsync_PersisteThemeKindThemeVersionYSenderEnLaFila()
    {
        await SeedTenantAsync();

        await using var ctx = NewContext();
        var writer = new NotificationDeliveryLogWriter(ctx);

        await writer.WriteAsync(
            new NotificationDeliveryLogEntry(
                TenantId,
                "security.invitation",
                "flit_smtp",
                "destinatario@ejemplo.test",
                Success: true,
                FailureReason: null,
                DurationMs: 42)
            {
                ThemeKind = "brand",
                ThemeVersion = 7,
                SenderName = "Movilidad Andina",
                SenderEmail = "no-reply@flitsas.online",
            },
            TestContext.Current.CancellationToken);

        await using var check = NewContext();
        var row = await check.NotificationDeliveryLogs.AsNoTracking()
            .Where(l => l.TenantId == TenantId)
            .SingleAsync(TestContext.Current.CancellationToken);

        row.ThemeKind.Should().Be("brand");
        row.ThemeVersion.Should().Be(7);
        row.SenderName.Should().Be("Movilidad Andina");
        row.SenderEmail.Should().Be("no-reply@flitsas.online");
    }

    [PostgresFact]
    public async Task AC5_WriteAsync_ConThemeKindNulo_PersisteFilaSinTema()
    {
        await SeedTenantAsync();

        await using var ctx = NewContext();
        var writer = new NotificationDeliveryLogWriter(ctx);

        await writer.WriteAsync(
            new NotificationDeliveryLogEntry(
                TenantId, "security.forgot-password", "flit_smtp", "otro@ejemplo.test",
                Success: true, FailureReason: null, DurationMs: 10),
            TestContext.Current.CancellationToken);

        await using var check = NewContext();
        var row = await check.NotificationDeliveryLogs.AsNoTracking()
            .Where(l => l.TenantId == TenantId)
            .SingleAsync(TestContext.Current.CancellationToken);

        row.ThemeKind.Should().BeNull();
        row.ThemeVersion.Should().BeNull();
        row.SenderName.Should().BeNull();
        row.SenderEmail.Should().BeNull();
    }

    [PostgresFact]
    public async Task AC5_CheckThemeKind_RechazaValorFueraDeFlitOBrand()
    {
        await SeedTenantAsync();

        await using var ctx = NewContext();
        ctx.NotificationDeliveryLogs.Add(new NotificationDeliveryLogEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            TemplateKey = "security.invitation",
            Channel = "flit_smtp",
            Recipient = "destinatario@ejemplo.test",
            Result = "enviado",
            DurationMs = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ThemeKind = "otro",
        });

        var act = async () => await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        ex.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be("23514", "el CHECK ck_notification_delivery_logs_theme_kind debe rechazar 'otro'");
    }
}

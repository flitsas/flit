using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.Settings;
using Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.Settings;

/// <summary>
/// HU #13344 (Epic #13316) — con Consultas remoto, lo que el SuperAdmin guarda en la pantalla de siempre también queda
/// en Consultas. Primero Consultas: si no responde, no se guarda nada en FLIT.
/// </summary>
public sealed class ConsultasConfigSyncTests
{
    private static readonly Guid ChangedBy = Guid.NewGuid();

    [Fact]
    public async Task ConConsultasArriba_LaCadenaNuevaVaAConsultas_YSeGuardaEnFlit()
    {
        var (db, tenantId) = await SeedAsync();
        var sync = new SyncFalso();

        await using (var act = NewContext(db))
        {
            var result = await Handler(act, sync).HandleAsync(Comando(tenantId, failover: 9000), TestContext.Current.CancellationToken);
            result.IsValid.Should().BeTrue();
        }

        sync.Recibido.Should().ContainSingle();
        var (tenant, settings) = sync.Recibido.Single();
        tenant.Should().Be(tenantId);
        settings.RuntFailoverTimeoutMs.Should().Be(9000);
        settings.ConsultationProviderConfig.ByKind["vehicle_plate"].Should().BeEquivalentTo(new ConsultationProviderSelection("verifik", ["kyverum_runt"]));

        await using var verify = NewContext(db);
        (await verify.TenantOperationalPolicies.SingleAsync(p => p.TenantId == tenantId, TestContext.Current.CancellationToken))
            .RuntFailoverTimeoutMs.Should().Be(9000);
    }

    [Fact]
    public async Task ConConsultasCaido_NoSeGuardaNada_YLaPantallaRecibeElError()
    {
        var (db, tenantId) = await SeedAsync();

        await using (var act = NewContext(db))
        {
            var result = await Handler(act, new SyncFalso(falla: true)).HandleAsync(Comando(tenantId, failover: 9000), TestContext.Current.CancellationToken);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.Field == "consultationProviderConfig");
        }

        await using var verify = NewContext(db);
        (await verify.TenantOperationalPolicies.SingleAsync(p => p.TenantId == tenantId, TestContext.Current.CancellationToken))
            .RuntFailoverTimeoutMs.Should().Be(4000, "nada se guardó");
        (await verify.TenantConfigAuditLogs.CountAsync(a => a.TenantId == tenantId, TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task SinConsultasRemoto_NoHaySincronizacion_YTodoQuedaComoAntes()
    {
        var (db, tenantId) = await SeedAsync();

        await using var act = NewContext(db);
        var result = await new UpdateTenantSettingsHandler(new TenantSettingsRepository(act, NullAuditContextAccessor.Instance), new StubTenantProductFlags())
            .HandleAsync(Comando(tenantId, failover: 9000), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
    }

    private static UpdateTenantSettingsHandler Handler(FlitDbContext ctx, IConsultasConfigSync sync) =>
        new(new TenantSettingsRepository(ctx, NullAuditContextAccessor.Instance), new StubTenantProductFlags(), sync);

    private static UpdateTenantSettingsCommand Comando(Guid tenantId, int failover) => new()
    {
        TenantId = tenantId,
        ChangedBy = ChangedBy,
        Request = new UpdateTenantSettingsRequest(
            new SwitchesMatricula(true, true, false),
            BaulFirmasActivo: false,
            EnrutamientoSMTP: "FLIT_SMTP",
            NotificationTarget: "RADICADOR",
            MetodosRecaudo: [],
            RuntFailoverTimeoutMs: failover,
            ConsultationProviderConfig: new Dictionary<string, ConsultationProviderChoice>
            {
                ["vehicle_plate"] = new("verifik", ["kyverum_runt"]),
            }),
    };

    private static async Task<(string Db, Guid TenantId)> SeedAsync()
    {
        var db = $"flit-consultas-sync-{Guid.NewGuid()}";
        var tenantId = Guid.NewGuid();
        await using var seed = NewContext(db);
        seed.TenantOperationalPolicies.Add(new TenantOperationalPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AllowInitialRegistration = true,
            AllowMiscNewVehicles = true,
            NotificationChannel = "flit_smtp",
            NotificationTarget = "RADICADOR",
            PaymentMethods = "[]",
            RuntProviderStrategy = "verifik",
            RuntFailoverTimeoutMs = 4000,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await seed.SaveChangesAsync();
        return (db, tenantId);
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private sealed class SyncFalso(bool falla = false) : IConsultasConfigSync
    {
        public List<(Guid TenantId, TenantSettings Settings)> Recibido { get; } = [];

        public Task SyncAsync(Guid tenantId, TenantSettings settings, CancellationToken ct)
        {
            if (falla)
                throw new ConsultasConfigSyncException("Consultas respondió Unavailable");
            Recibido.Add((tenantId, settings));
            return Task.CompletedTask;
        }
    }
}

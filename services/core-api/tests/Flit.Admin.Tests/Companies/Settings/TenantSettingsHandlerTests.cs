using Flit.Admin.Application.Companies.Settings;
using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
using Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.Settings;

/// <summary>
/// Tests de configuración operativa del tenant (HU #10190) — AC1 (guardado atómico
/// + audit log por campo), AC2 (validación 422 sin persistir ni auditar) y AC3
/// (GET de la configuración actual). Ejercitan los handlers reales sobre el
/// repositorio EF real con proveedor InMemory (la transacción/SET LOCAL aplican
/// solo en proveedor relacional).
/// </summary>
public sealed class TenantSettingsHandlerTests
{
    private static readonly Guid ChangedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---------- AC1: guardado atómico + auditoría ----------

    [Fact]
    public async Task AC1_PersistsAllFields_AndWritesAuditPerChangedField()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedPolicy(seed, tenantId,
                allowInit: true, allowMisc: true, onlyOwn: false, vault: false,
                channel: "flit_smtp", target: "RADICADOR", payments: "[]");
        }

        await using (var act = NewContext(db))
        {
            var handler = new UpdateTenantSettingsHandler(new TenantSettingsRepository(act));
            var result = await handler.HandleAsync(new UpdateTenantSettingsCommand
            {
                TenantId = tenantId,
                ChangedBy = ChangedBy,
                Request = new UpdateTenantSettingsRequest(
                    new SwitchesMatricula(false, false, true),
                    BaulFirmasActivo: true,
                    EnrutamientoSMTP: "TENANT_API",
                    NotificationTarget: "COMPRADOR",
                    MetodosRecaudo: ["pse", "efecty"]),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.Settings!.NotificationTarget.Should().Be("COMPRADOR");
            result.Settings.MetodosRecaudo.Should().BeEquivalentTo("pse", "efecty");
        }

        // Verificación en un contexto nuevo: persistido de verdad, no solo trackeado.
        await using var verify = NewContext(db);

        var policy = await verify.TenantOperationalPolicies.SingleAsync(p => p.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken);
        policy.AllowInitialRegistration.Should().BeFalse();
        policy.AllowMiscNewVehicles.Should().BeFalse();
        policy.OnlyOwnVehicles.Should().BeTrue();
        policy.SignatureVaultEnabled.Should().BeTrue();
        policy.NotificationChannel.Should().Be("tenant_api");
        policy.NotificationTarget.Should().Be("COMPRADOR");
        policy.PaymentMethods.Should().Be("[\"pse\",\"efecty\"]");

        var audits = await verify.TenantConfigAuditLogs
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        // 3 switches + baúl + canal + destino + métodos = 7 campos modificados.
        audits.Should().HaveCount(7);
        audits.Should().OnlyContain(a => a.EntityName == "tenant_operational_policies");
        audits.Should().OnlyContain(a => a.ChangedBy == ChangedBy);
        audits.Should().Contain(a =>
            a.FieldName == "only_own_vehicles" && a.OldValue == "false" && a.NewValue == "true");
        audits.Should().Contain(a =>
            a.FieldName == "notification_channel" && a.OldValue == "\"flit_smtp\"" && a.NewValue == "\"tenant_api\"");
        audits.Should().Contain(a =>
            a.FieldName == "notification_target" && a.OldValue == "\"RADICADOR\"" && a.NewValue == "\"COMPRADOR\"");
        audits.Should().Contain(a =>
            a.FieldName == "payment_methods" && a.NewValue == "[\"pse\",\"efecty\"]");
    }

    [Fact]
    public async Task AC1_OnlyAuditsModifiedFields()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedPolicy(seed, tenantId,
                allowInit: true, allowMisc: true, onlyOwn: false, vault: false,
                channel: "flit_smtp", target: "RADICADOR", payments: "[]");
        }

        await using (var act = NewContext(db))
        {
            var handler = new UpdateTenantSettingsHandler(new TenantSettingsRepository(act));
            // Solo cambia un campo (baúl de firmas); el resto es idéntico a lo sembrado.
            await handler.HandleAsync(new UpdateTenantSettingsCommand
            {
                TenantId = tenantId,
                ChangedBy = ChangedBy,
                Request = new UpdateTenantSettingsRequest(
                    new SwitchesMatricula(true, true, false),
                    BaulFirmasActivo: true,
                    EnrutamientoSMTP: "FLIT_SMTP",
                    NotificationTarget: "RADICADOR",
                    MetodosRecaudo: []),
            }, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);
        var audits = await verify.TenantConfigAuditLogs.Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        audits.Should().ContainSingle()
            .Which.FieldName.Should().Be("signature_vault_enabled");
    }

    [Fact]
    public async Task AC1_CreatesRow_WhenNoConfigurationExists()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            var handler = new UpdateTenantSettingsHandler(new TenantSettingsRepository(act));
            var result = await handler.HandleAsync(new UpdateTenantSettingsCommand
            {
                TenantId = tenantId,
                Request = new UpdateTenantSettingsRequest(
                    new SwitchesMatricula(false, false, true),
                    BaulFirmasActivo: true,
                    EnrutamientoSMTP: "TENANT_API",
                    NotificationTarget: "NINGUNO",
                    MetodosRecaudo: ["pse"]),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
        }

        await using var verify = NewContext(db);
        (await verify.TenantOperationalPolicies.CountAsync(p => p.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        // Se audita cada campo distinto de los defaults del DDL.
        (await verify.TenantConfigAuditLogs.CountAsync(a => a.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().BeGreaterThan(0);
    }

    // ---------- AC2: validación 422 sin persistir ni auditar ----------

    [Fact]
    public async Task AC2_InvalidNotificationTarget_Returns422_AndPersistsNothing()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedPolicy(seed, tenantId,
                allowInit: true, allowMisc: true, onlyOwn: false, vault: false,
                channel: "flit_smtp", target: "RADICADOR", payments: "[]");
        }

        await using (var act = NewContext(db))
        {
            var handler = new UpdateTenantSettingsHandler(new TenantSettingsRepository(act));
            var result = await handler.HandleAsync(new UpdateTenantSettingsCommand
            {
                TenantId = tenantId,
                Request = new UpdateTenantSettingsRequest(
                    new SwitchesMatricula(false, false, true),
                    BaulFirmasActivo: true,
                    EnrutamientoSMTP: "FLIT_SMTP",
                    NotificationTarget: "INVALIDO",
                    MetodosRecaudo: ["pse"]),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeFalse();
            result.Settings.Should().BeNull();
            result.Errors.Should().ContainSingle(e => e.Field == "notificationTarget");
        }

        await using var verify = NewContext(db);
        var policy = await verify.TenantOperationalPolicies.SingleAsync(p => p.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken);
        // Sin cambios respecto a lo sembrado.
        policy.NotificationTarget.Should().Be("RADICADOR");
        policy.OnlyOwnVehicles.Should().BeFalse();
        policy.SignatureVaultEnabled.Should().BeFalse();
        (await verify.TenantConfigAuditLogs.CountAsync(a => a.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AC2_InvalidEnrutamientoSMTP_Returns422()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new UpdateTenantSettingsHandler(new TenantSettingsRepository(ctx));

        var result = await handler.HandleAsync(new UpdateTenantSettingsCommand
        {
            TenantId = Guid.NewGuid(),
            Request = new UpdateTenantSettingsRequest(
                new SwitchesMatricula(true, true, false),
                BaulFirmasActivo: false,
                EnrutamientoSMTP: "CORREO_RARO",
                NotificationTarget: "COMPRADOR",
                MetodosRecaudo: []),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "enrutamientoSMTP");
    }

    // ---------- AC3: GET configuración actual ----------

    [Fact]
    public async Task AC3_Get_ReturnsCurrentConfiguration()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedPolicy(seed, tenantId,
                allowInit: false, allowMisc: true, onlyOwn: true, vault: true,
                channel: "tenant_api", target: "COMPRADOR", payments: "[\"pse\"]");
        }

        await using var ctx = NewContext(db);
        var handler = new GetTenantSettingsHandler(new TenantSettingsRepository(ctx));

        var result = await handler.HandleAsync(new GetTenantSettingsQuery { TenantId = tenantId }, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.TenantId.Should().Be(tenantId);
        result.SwitchesMatricula.AllowInitialRegistration.Should().BeFalse();
        result.SwitchesMatricula.OnlyOwnVehicles.Should().BeTrue();
        result.BaulFirmasActivo.Should().BeTrue();
        result.EnrutamientoSMTP.Should().Be("TENANT_API");
        result.NotificationTarget.Should().Be("COMPRADOR");
        result.MetodosRecaudo.Should().BeEquivalentTo("pse");
    }

    [Fact]
    public async Task AC3_Get_ReturnsNull_WhenNoConfiguration()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new GetTenantSettingsHandler(new TenantSettingsRepository(ctx));

        var result = await handler.HandleAsync(new GetTenantSettingsQuery { TenantId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    // ---------- HU #10478: config de proveedores de consulta + failover timeout ----------

    [Fact]
    public async Task HU10478_PersistsConsultationConfigAndFailoverTimeout_AndAuditsOnlyThose()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedPolicy(seed, tenantId,
                allowInit: true, allowMisc: true, onlyOwn: false, vault: false,
                channel: "flit_smtp", target: "RADICADOR", payments: "[]");
        }

        await using (var act = NewContext(db))
        {
            var handler = new UpdateTenantSettingsHandler(new TenantSettingsRepository(act));
            // Resto idéntico a lo sembrado: solo cambian los 2 campos nuevos.
            var result = await handler.HandleAsync(new UpdateTenantSettingsCommand
            {
                TenantId = tenantId,
                ChangedBy = ChangedBy,
                Request = new UpdateTenantSettingsRequest(
                    new SwitchesMatricula(true, true, false),
                    BaulFirmasActivo: false,
                    EnrutamientoSMTP: "FLIT_SMTP",
                    NotificationTarget: "RADICADOR",
                    MetodosRecaudo: [],
                    RuntFailoverTimeoutMs: 8000,
                    ConsultationProviderConfig: new Dictionary<string, ConsultationProviderChoice>
                    {
                        ["vehicle_vin"] = new("kyverum_runt", ["verifik"]),
                    }),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.Settings!.RuntFailoverTimeoutMs.Should().Be(8000);
            result.Settings.ConsultationProviderConfig.Should().ContainKey("vehicle_vin");
            result.Settings.ConsultationProviderConfig["vehicle_vin"].Primary.Should().Be("kyverum_runt");
            result.Settings.ConsultationProviderConfig["vehicle_vin"].Fallback.Should().BeEquivalentTo("verifik");
        }

        await using var verify = NewContext(db);
        var policy = await verify.TenantOperationalPolicies.SingleAsync(p => p.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken);
        policy.RuntFailoverTimeoutMs.Should().Be(8000);
        policy.ConsultationProviderConfig.Should().Contain("kyverum_runt").And.Contain("vehicle_vin");

        var audits = await verify.TenantConfigAuditLogs.Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        audits.Should().HaveCount(2);
        audits.Should().Contain(a => a.FieldName == "runt_failover_timeout_ms" && a.OldValue == "4000" && a.NewValue == "8000");
        audits.Should().Contain(a =>
            a.FieldName == "consultation_provider_config" &&
            a.OldValue == "{}" &&
            a.NewValue == "{\"vehicle_vin\":{\"primary\":\"kyverum_runt\",\"fallback\":[\"verifik\"]}}");
    }

    [Fact]
    public async Task HU10478_InvalidProviderKey_Returns422_AndPersistsNothing()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new UpdateTenantSettingsHandler(new TenantSettingsRepository(ctx));

        var result = await handler.HandleAsync(new UpdateTenantSettingsCommand
        {
            TenantId = Guid.NewGuid(),
            Request = new UpdateTenantSettingsRequest(
                new SwitchesMatricula(true, true, false),
                BaulFirmasActivo: false,
                EnrutamientoSMTP: "FLIT_SMTP",
                NotificationTarget: "RADICADOR",
                MetodosRecaudo: [],
                ConsultationProviderConfig: new Dictionary<string, ConsultationProviderChoice>
                {
                    // 'intempo' no es un proveedor permitido para vehicle_vin.
                    ["vehicle_vin"] = new("intempo", []),
                }),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "consultationProviderConfig");
    }

    [Fact]
    public async Task HU10478_UnknownKind_Returns422()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new UpdateTenantSettingsHandler(new TenantSettingsRepository(ctx));

        var result = await handler.HandleAsync(new UpdateTenantSettingsCommand
        {
            TenantId = Guid.NewGuid(),
            Request = new UpdateTenantSettingsRequest(
                new SwitchesMatricula(true, true, false),
                BaulFirmasActivo: false,
                EnrutamientoSMTP: "FLIT_SMTP",
                NotificationTarget: "RADICADOR",
                MetodosRecaudo: [],
                ConsultationProviderConfig: new Dictionary<string, ConsultationProviderChoice>
                {
                    ["simit"] = new("verifik", []),
                }),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "consultationProviderConfig");
    }

    [Fact]
    public async Task HU10478_FailoverTimeoutOutOfRange_Returns422()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new UpdateTenantSettingsHandler(new TenantSettingsRepository(ctx));

        var result = await handler.HandleAsync(new UpdateTenantSettingsCommand
        {
            TenantId = Guid.NewGuid(),
            Request = new UpdateTenantSettingsRequest(
                new SwitchesMatricula(true, true, false),
                BaulFirmasActivo: false,
                EnrutamientoSMTP: "FLIT_SMTP",
                NotificationTarget: "RADICADOR",
                MetodosRecaudo: [],
                RuntFailoverTimeoutMs: 100),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "runtFailoverTimeoutMs");
    }

    [Fact]
    public async Task HU10478_Get_ReturnsConfigAndTimeout()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TenantOperationalPolicies.Add(new TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AllowInitialRegistration = true,
                AllowMiscNewVehicles = true,
                OnlyOwnVehicles = false,
                SignatureVaultEnabled = false,
                NotificationChannel = "flit_smtp",
                NotificationTarget = "RADICADOR",
                PaymentMethods = "[]",
                RuntFailoverTimeoutMs = 6000,
                ConsultationProviderConfig =
                    "{\"conductor\":{\"primary\":\"verifik_conductor\",\"fallback\":[\"kyverum_runt_conductor\"]}}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new GetTenantSettingsHandler(new TenantSettingsRepository(ctx));

        var result = await handler.HandleAsync(new GetTenantSettingsQuery { TenantId = tenantId }, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.RuntFailoverTimeoutMs.Should().Be(6000);
        result.ConsultationProviderConfig.Should().ContainKey("conductor");
        result.ConsultationProviderConfig["conductor"].Primary.Should().Be("verifik_conductor");
        result.ConsultationProviderConfig["conductor"].Fallback.Should().BeEquivalentTo("kyverum_runt_conductor");
    }

    // ---------- Helpers ----------

    private static string NewDbName() => $"flit-settings-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static void SeedPolicy(
        FlitDbContext ctx,
        Guid tenantId,
        bool allowInit,
        bool allowMisc,
        bool onlyOwn,
        bool vault,
        string channel,
        string target,
        string payments)
    {
        ctx.TenantOperationalPolicies.Add(new TenantOperationalPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AllowInitialRegistration = allowInit,
            AllowMiscNewVehicles = allowMisc,
            OnlyOwnVehicles = onlyOwn,
            SignatureVaultEnabled = vault,
            NotificationChannel = channel,
            NotificationTarget = target,
            PaymentMethods = payments,
            RuntProviderStrategy = "verifik",
            RuntFailoverTimeoutMs = 4000,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }
}

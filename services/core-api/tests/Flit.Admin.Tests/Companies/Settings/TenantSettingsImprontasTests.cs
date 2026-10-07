using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.Settings;
using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
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
/// HU #13400 (Feature #13398) — parámetro por compañía «generación automática de improntas».
/// AC1 default true, AC3 auditoría old/new, AC4 sin auditoría si no cambia, AC5 independencia entre
/// compañías. AC2 (403 a roles distintos de SuperAdmin) se cubre en
/// <see cref="AdminCompaniesSettingsAuthorizationTests"/> y en AdminCompanyReservedSectionsTests.
/// </summary>
public sealed class TenantSettingsImprontasTests
{
    private static readonly Guid ChangedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void AC1_Default_IsEnabled()
    {
        TenantSettings.Default(Guid.NewGuid()).GenerateImprontas.Should().BeTrue();
        new TenantOperationalPolicy().GenerateImprontas.Should().BeTrue();
    }

    [Fact]
    public async Task AC1_ExistingPolicyWithoutChanges_ReadsEnabled()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        await Seed(db, tenantId);

        await using var ctx = NewContext(db);
        var get = new GetTenantSettingsHandler(Repo(ctx), new StubTenantProductFlags());
        var response = await get.HandleAsync(new GetTenantSettingsQuery { TenantId = tenantId }, TestContext.Current.CancellationToken);

        response!.GeneracionImprontas.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_ChangingValue_PersistsAndAuditsOldAndNew()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        await Seed(db, tenantId);

        await using (var act = NewContext(db))
        {
            var result = await Update(act, tenantId, generacionImprontas: false);
            result.IsValid.Should().BeTrue();
            result.Settings!.GeneracionImprontas.Should().BeFalse();
        }

        await using var verify = NewContext(db);
        (await verify.TenantOperationalPolicies.SingleAsync(p => p.TenantId == tenantId, TestContext.Current.CancellationToken))
            .GenerateImprontas.Should().BeFalse();

        var audits = await verify.TenantConfigAuditLogs
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(TestContext.Current.CancellationToken);
        var audit = audits.Should().ContainSingle(a => a.FieldName == "generate_improntas").Subject;
        audit.OldValue.Should().Be("true");
        audit.NewValue.Should().Be("false");
        audit.ChangedBy.Should().Be(ChangedBy);
        audit.TenantId.Should().Be(tenantId);
        audit.ChangedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AC4_SameValue_DoesNotCreateAuditRecord()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        await Seed(db, tenantId);

        await using (var act = NewContext(db))
        {
            var result = await Update(act, tenantId, generacionImprontas: true);
            result.IsValid.Should().BeTrue();
        }

        await using var verify = NewContext(db);
        (await verify.TenantConfigAuditLogs.CountAsync(
            a => a.TenantId == tenantId && a.FieldName == "generate_improntas",
            TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AC4_OmittedValue_PreservesPreviousAndDoesNotAudit()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        await Seed(db, tenantId, generate: false);

        await using (var act = NewContext(db))
        {
            var result = await Update(act, tenantId, generacionImprontas: null);
            result.Settings!.GeneracionImprontas.Should().BeFalse();
        }

        await using var verify = NewContext(db);
        (await verify.TenantOperationalPolicies.SingleAsync(p => p.TenantId == tenantId, TestContext.Current.CancellationToken))
            .GenerateImprontas.Should().BeFalse();
        (await verify.TenantConfigAuditLogs.CountAsync(
            a => a.FieldName == "generate_improntas", TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AC5_ChangingCompanyA_DoesNotAffectCompanyB()
    {
        var db = NewDbName();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await Seed(db, tenantA);
        await Seed(db, tenantB);

        await using (var act = NewContext(db))
        {
            await Update(act, tenantA, generacionImprontas: false);
        }

        await using var verify = NewContext(db);
        var a = await verify.TenantOperationalPolicies.SingleAsync(p => p.TenantId == tenantA, TestContext.Current.CancellationToken);
        var b = await verify.TenantOperationalPolicies.SingleAsync(p => p.TenantId == tenantB, TestContext.Current.CancellationToken);
        a.GenerateImprontas.Should().BeFalse();
        b.GenerateImprontas.Should().BeTrue();
        (await verify.TenantConfigAuditLogs.CountAsync(
            l => l.TenantId == tenantB, TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AC1_NewPolicyCreatedByFirstSave_WithDisabledValue_PersistsFalse()
    {
        // Compañía sin fila previa: el INSERT debe respetar un false explícito.
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            await Update(act, tenantId, generacionImprontas: false);
        }

        await using var verify = NewContext(db);
        (await verify.TenantOperationalPolicies.SingleAsync(p => p.TenantId == tenantId, TestContext.Current.CancellationToken))
            .GenerateImprontas.Should().BeFalse();
    }

    private static TenantSettingsRepository Repo(FlitDbContext ctx) =>
        new(ctx, NullAuditContextAccessor.Instance);

    private static Task<UpdateTenantSettingsResult> Update(FlitDbContext ctx, Guid tenantId, bool? generacionImprontas) =>
        new UpdateTenantSettingsHandler(Repo(ctx), new StubTenantProductFlags()).HandleAsync(
            new UpdateTenantSettingsCommand
            {
                TenantId = tenantId,
                ChangedBy = ChangedBy,
                Request = new UpdateTenantSettingsRequest(
                    new SwitchesMatricula(true, true, false),
                    BaulFirmasActivo: false,
                    EnrutamientoSMTP: "FLIT_SMTP",
                    NotificationTarget: "RADICADOR",
                    MetodosRecaudo: [],
                    GeneracionImprontas: generacionImprontas),
            },
            TestContext.Current.CancellationToken);

    private static async Task Seed(string db, Guid tenantId, bool generate = true)
    {
        await using var ctx = NewContext(db);
        ctx.TenantOperationalPolicies.Add(new TenantOperationalPolicy
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
            GenerateImprontas = generate,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static string NewDbName() => $"improntas-{Guid.NewGuid():N}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}

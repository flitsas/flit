using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>HU #12348 y #12409 — gate de radicación (AC6/AC8, PostgreSQL real).</summary>
public sealed class ProcedureRadicationGateIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid UserId = new("88888888-8888-4888-8888-888888888888");

    private static ProcedureRadicationGate NewGate(FlitDbContext ctx) =>
        new(
            ctx,
            new CompanyHierarchyRepository(ctx),
            new EffectiveTransitOfficeListResolver(
                new CompanyHierarchyRepository(ctx),
                new TransitGrantRepository(ctx, NullAuditContextAccessor.Instance),
                new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance),
                new DbTransitOfficeOperationalStatusReader(ctx),
                NullLogger<EffectiveTransitOfficeListResolver>.Instance));

    [PostgresFact]
    public async Task AC1_OT_en_lista_efectiva_permite_crear()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var ctx = NewContext())
        {
            await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot1);
        }

        await using var read = NewContext();
        var gate = NewGate(read);

        var result = await gate.ValidateCreateAsync(
            HierarchyScenario.C1, UserId, HierarchyScenario.Ot1);

        result.IsAllowed.Should().BeTrue();
    }

    [PostgresFact]
    public async Task AC2_OT_no_permitido_rechaza_y_audita()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var ctx = NewContext())
        {
            await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot2);
        }

        await using var read = NewContext();
        var gate = NewGate(read);

        var result = await gate.ValidateCreateAsync(
            HierarchyScenario.C1, UserId, HierarchyScenario.Ot1);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ProcedureRadicationDenialReasons.OtNotPermitted);

        var denial = await read.ProcedureRadicationGateDenials.AsNoTracking().SingleAsync();
        denial.TenantId.Should().Be(HierarchyScenario.C1);
        denial.TransitOfficeId.Should().Be(HierarchyScenario.Ot1);
        denial.DenialReason.Should().Be(ProcedureRadicationDenialReasons.OtNotPermitted);
    }

    [PostgresFact]
    public async Task AC1_Cabeza_inactiva_rechaza_con_red_inactiva()
    {
        await HierarchyScenario.SeedAsync(Fixture);

        await using (var ctx = NewContext())
        {
            var head = await ctx.Tenants.SingleAsync(t => t.Id == HierarchyScenario.P);
            head.IsActive = false;
            await ctx.SaveChangesAsync();
        }

        await using var read = NewContext();
        var gate = NewGate(read);

        var result = await gate.ValidateCreateAsync(
            HierarchyScenario.C1, UserId, HierarchyScenario.Ot1);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ProcedureRadicationDenialReasons.NetworkInactive);
    }

    [PostgresFact]
    public async Task AC3_Compania_inactiva_rechaza_aun_sin_OT()
    {
        await HierarchyScenario.SeedAsync(Fixture);

        await using (var ctx = NewContext())
        {
            var tenant = await ctx.Tenants.SingleAsync(t => t.Id == HierarchyScenario.S);
            tenant.IsActive = false;
            await ctx.SaveChangesAsync();
        }

        await using var read = NewContext();
        var gate = NewGate(read);

        var result = await gate.ValidateCreateAsync(HierarchyScenario.S, UserId, transitOfficeId: null);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ProcedureRadicationDenialReasons.TenantInactive);
    }

    [PostgresFact]
    public async Task AC7_Hijo_MB_hacia_OT_bloqueado_es_rechazado()
    {
        await TransitNetworkSeed.SeedMarcaBlancaNetworkAsync(Fixture);

        await using (var seedUser = NewContext())
        {
            await TransitNetworkSeed.SeedUserAsync(seedUser, UserId, TransitNetworkSeed.MbHead, "gate-mb@flit.test");
        }

        await using (var ctx = NewContext())
        {
            await new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance)
                .AddBlockAsync(TransitNetworkSeed.MbHead, TransitNetworkSeed.Ot1, UserId, null);
        }

        await using var read = NewContext();
        var gate = NewGate(read);

        var result = await gate.ValidateCreateAsync(
            TransitNetworkSeed.MbC1, UserId, TransitNetworkSeed.Ot1);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ProcedureRadicationDenialReasons.OtNotPermitted);
    }
}

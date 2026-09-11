using Flit.Admin.Domain.OtClientProcedures;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>HU #12350 AC7 — la bandeja del OT deriva de trámites recibidos, no de grants vigentes.</summary>
public sealed class OtBandejaAfterGrantRevokeIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    [PostgresFact]
    public async Task AC7_Trámites_entregados_siguen_visibles_tras_revocar_grant()
    {
        await HierarchyScenario.SeedAsync(Fixture);

        await using var ctx = NewContext();
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        var before = await repo.ListAsync(
            HierarchyScenario.O,
            new OtClientProcedureFilter { Page = 1, PageSize = 100 });

        // Todos los clientes entregaron a Ot1 en la semilla, no solo C1 con grant.
        before.Items.Select(i => i.ClientTenantId).Should().BeEquivalentTo(HierarchyScenario.Clients);

        await using (var revokeCtx = NewContext())
        {
            var grant = await revokeCtx.TenantTransitOfficeGrants
                .SingleAsync(g => g.TenantId == HierarchyScenario.C1 && g.TransitOfficeId == HierarchyScenario.Ot1);
            revokeCtx.TenantTransitOfficeGrants.Remove(grant);
            await revokeCtx.SaveChangesAsync();
        }

        await using var afterCtx = NewContext();
        var afterRepo = new OtClientProcedureRepository(afterCtx, new NullTramiteTransitionPublisher());
        var after = await afterRepo.ListAsync(
            HierarchyScenario.O,
            new OtClientProcedureFilter { Page = 1, PageSize = 100 });

        after.Items.Should().Contain(i =>
            i.ClientTenantId == HierarchyScenario.C1
            && i.Id == HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1));
        after.Items.Select(i => i.ClientTenantId).Should().BeEquivalentTo(HierarchyScenario.Clients);
    }

    [PostgresFact]
    public async Task AC2_Trámite_entregado_conserva_OT_y_estado()
    {
        await HierarchyScenario.SeedAsync(Fixture);

        await using (var revokeCtx = NewContext())
        {
            await revokeCtx.TenantTransitOfficeGrants.ExecuteDeleteAsync();
            await revokeCtx.SaveChangesAsync();
        }

        await using var ctx = NewContext();
        var row = await ctx.ProcedureInstances.AsNoTracking()
            .SingleAsync(p => p.Id == HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1));

        row.TransitOfficeId.Should().Be(HierarchyScenario.Ot1);
        row.Status.Should().Be(TramiteEstado.Entregado);
    }
}

using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.PlatePreassign;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// Bug local 2026-09-16 — hijas de Concesión sin grant propio, sobre PostgreSQL real: la elegibilidad
/// de preasignación de placa debe resolver el OT efectivo por jerarquía (mismo criterio que
/// <see cref="EffectiveTransitOfficeListResolver"/>), no solo el grant directo del tenant.
/// </summary>
public sealed class PlateRangeHierarchyEligibilityIntegrationTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private static EffectiveTransitOfficeListResolver NewEffectiveResolver(FlitDbContext ctx) =>
        new(
            new CompanyHierarchyRepository(ctx),
            new TransitGrantRepository(ctx, NullAuditContextAccessor.Instance),
            new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance),
            new DbTransitOfficeOperationalStatusReader(ctx),
            NullLogger<EffectiveTransitOfficeListResolver>.Instance);

    [PostgresFact]
    public async Task Hija_Elegible_Por_OtEfectivoDeLaCabeza_Hermana_De_Otra_Red_No()
    {
        // C2 es hija de P (Concesión) y, en la semilla base, su grant propio es a Ot2 (no Ot1).
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var ctx = NewContext())
        {
            // La cabeza P recibe grant a Ot1: el OT efectivo de TODOS sus hijos pasa a ser [Ot1].
            await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot1);

            ctx.OtRequirements.Add(new OtRequirementsEntity
            {
                Id = Guid.NewGuid(),
                TenantId = HierarchyScenario.O,
                TransitOfficeId = HierarchyScenario.Ot1,
                AllowPlatePreassign = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            ctx.TenantOperationalPolicies.Add(new TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = HierarchyScenario.C2,
                PlatePreassignEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            // X es un tenant ajeno a la red (sin padre): su flag propio no debe volverlo elegible
            // para Ot1 salvo que tenga su propio grant, que no tiene.
            ctx.TenantOperationalPolicies.Add(new TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = HierarchyScenario.X,
                PlatePreassignEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = NewContext();
        var repo = new PlateRangeRepository(read, NewEffectiveResolver(read));

        var c2Eligibility = await repo.EvaluateAssignmentEligibilityAsync(HierarchyScenario.C2, HierarchyScenario.Ot1);
        var xEligibility = await repo.EvaluateAssignmentEligibilityAsync(HierarchyScenario.X, HierarchyScenario.Ot1);

        c2Eligibility.Should().Be(PlateAssignmentEligibility.Allowed);
        xEligibility.Should().Be(PlateAssignmentEligibility.Misconfigured);

        var eligible = await repo.ListEligibleCompaniesAsync(HierarchyScenario.Ot1);
        var eligibleIds = eligible.Select(e => e.TenantId).ToList();

        eligibleIds.Should().Contain(HierarchyScenario.C2);
        eligibleIds.Should().NotContain(HierarchyScenario.X, "X no tiene padre ni grant propio a Ot1");
    }
}

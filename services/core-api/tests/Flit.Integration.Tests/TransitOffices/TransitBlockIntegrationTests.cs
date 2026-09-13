using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.TransitOffices.TransitBlocks.AddTransitBlock;
using Flit.Admin.Application.Companies.TransitOffices.TransitBlocks.RemoveTransitBlock;
using Flit.Admin.Domain.Companies.Create;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>HU #12407 — bloqueos de OT para Marca Blanca (AC8, PostgreSQL real).</summary>
public sealed class TransitBlockIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Actor = new("77777777-7777-4777-8777-777777777777");

    [PostgresFact]
    public async Task AC1_y_AC5_Alta_y_baja_de_bloqueo_dejan_auditoria_con_listas_anterior_y_nueva()
    {
        await TransitNetworkSeed.SeedMarcaBlancaNetworkAsync(Fixture);

        await using (var seedUser = NewContext())
        {
            await TransitNetworkSeed.SeedUserAsync(seedUser, Actor, TransitNetworkSeed.MbHead, "actor-mb@flit.test");
        }

        await using (var ctx = NewContext())
        {
            var handler = new AddTransitBlockHandler(
                new CompanyHierarchyRepository(ctx),
                new DbTransitOfficeCatalog(ctx),
                new DbTransitOfficeOperationalStatusReader(ctx),
                new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance));

            var add = await handler.HandleAsync(
                new AddTransitBlockCommand(TransitNetworkSeed.MbHead, TransitNetworkSeed.Ot1, Actor));

            add.IsValid.Should().BeTrue();
            add.Added.Should().BeTrue();
        }

        await using (var ctx = NewContext())
        {
            (bool removed, string? error) = await new RemoveTransitBlockHandler(
                new CompanyHierarchyRepository(ctx),
                new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance))
                .HandleAsync(new RemoveTransitBlockCommand(TransitNetworkSeed.MbHead, TransitNetworkSeed.Ot1, Actor));

            removed.Should().BeTrue();
            error.Should().BeNull();
        }

        await using var check = NewContext();
        var audits = await check.TenantConfigAuditLogs.AsNoTracking()
            .Where(a => a.TenantId == TransitNetworkSeed.MbHead && a.EntityName == "tenant_transit_office_blocks")
            .OrderBy(a => a.ChangedAt)
            .ToListAsync();

        audits.Should().HaveCount(2);
        audits[0].OldValue.Should().Be("[]");
        audits[0].NewValue.Should().Contain(TransitNetworkSeed.Ot1.ToString());
        audits[1].OldValue.Should().Contain(TransitNetworkSeed.Ot1.ToString());
        audits[1].NewValue.Should().Be("[]");
    }

    [PostgresFact]
    public async Task AC3_Trigger_rechaza_bloqueo_en_cabeza_Concesion()
    {
        await HierarchyScenario.SeedAsync(Fixture);

        await using var ctx = NewContext();
        var act = async () =>
        {
            ctx.TenantTransitOfficeBlocks.Add(new Flit.Infrastructure.Persistence.Entities.Admin.TenantTransitOfficeBlock
            {
                Id = Guid.NewGuid(),
                TenantId = HierarchyScenario.P,
                TransitOfficeId = HierarchyScenario.Ot1,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        };

        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        ex.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.MessageText.Should().Contain("MARCA_BLANCA");
    }
}

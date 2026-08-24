using Flit.Admin.Application.OtClientProcedures.GetOtBandejaHealth;
using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// HU #10540 / R09 — cierre de circuito de la bandeja del OT. Verifica que un trámite entregado
/// hacia el OT aparece en la bandeja cuando existe grant vigente (AC1) y que, cuando NO hay grant,
/// el trámite queda invisible pero el diagnóstico (/health) lo reporta como "entregado sin grant"
/// con una causa clara (AC2), sin depender de datos sembrados de desarrollo.
/// </summary>
public sealed class OtBandejaHealthE2ETests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ProcedureTypeA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // AC1 — trámite real entregado hacia un OT con grant habilitado aparece listo en la bandeja
    // y el diagnóstico lo cuenta como "con grant".
    [Fact]
    public async Task AC1_DeliveredWithGrant_IsVisibleAndHealthy()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice, isEnabled: true);
            SeedCatalog(seed, ClientTenant, ProcedureTypeA, "Flota Andina S.A.S.", "Matrícula inicial");
            SeedDelivered(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        var bandeja = await new ListOtClientProceduresHandler(repo).HandleAsync(
            new ListOtClientProceduresQuery { OtTenantId = OtTenant, Status = TramiteEstado.Entregado },
            TestContext.Current.CancellationToken);

        bandeja.Data.Should().ContainSingle(p => p.Id == procedureId);

        var health = await new GetOtBandejaHealthHandler(repo).HandleAsync(
            new GetOtBandejaHealthQuery { OtTenantId = OtTenant },
            TestContext.Current.CancellationToken);

        health.TransitOfficeResolved.Should().BeTrue();
        health.DeliveredTotal.Should().Be(1);
        health.DeliveredWithGrant.Should().Be(1);
        health.DeliveredWithoutGrant.Should().Be(0);
        health.HasDeliveredWithoutGrant.Should().BeFalse();
    }

    // AC2 — entregado hacia un OT sin grant: la bandeja no lo muestra, pero el diagnóstico lo
    // reporta como "entregado sin grant" (causa clara para corregir la configuración).
    [Fact]
    public async Task AC2_DeliveredWithoutGrant_IsInvisibleButDiagnosable()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice, isEnabled: false);
            SeedCatalog(seed, ClientTenant, ProcedureTypeA, "Flota Andina S.A.S.", "Matrícula inicial");
            SeedDelivered(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        var bandeja = await new ListOtClientProceduresHandler(repo).HandleAsync(
            new ListOtClientProceduresQuery { OtTenantId = OtTenant },
            TestContext.Current.CancellationToken);

        bandeja.Data.Should().BeEmpty();

        var health = await new GetOtBandejaHealthHandler(repo).HandleAsync(
            new GetOtBandejaHealthQuery { OtTenantId = OtTenant },
            TestContext.Current.CancellationToken);

        health.TransitOfficeResolved.Should().BeTrue();
        health.DeliveredTotal.Should().Be(1);
        health.DeliveredWithGrant.Should().Be(0);
        health.DeliveredWithoutGrant.Should().Be(1);
        health.HasDeliveredWithoutGrant.Should().BeTrue();
    }

    // Edge — un tenant sin perfil OT no resuelve organismo: el diagnóstico lo indica sin fallar.
    [Fact]
    public async Task Health_WhenTenantHasNoTransitOfficeProfile_ReportsUnresolved()
    {
        var db = NewDbName();

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        var health = await new GetOtBandejaHealthHandler(repo).HandleAsync(
            new GetOtBandejaHealthQuery { OtTenantId = OtTenant },
            TestContext.Current.CancellationToken);

        health.TransitOfficeResolved.Should().BeFalse();
        health.DeliveredTotal.Should().Be(0);
        health.DeliveredWithoutGrant.Should().Be(0);
    }

    // Contrato — el diagnóstico separa correctamente varios entregados (con y sin grant) del mismo OT.
    [Fact]
    public async Task Health_CountsMixedDeliveries_SplitsByGrant()
    {
        var db = NewDbName();
        var grantedClient = ClientTenant;
        var orphanClient = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, grantedClient, TransitOffice, isEnabled: true);
            SeedGrant(seed, orphanClient, TransitOffice, isEnabled: false);
            SeedCatalog(seed, grantedClient, ProcedureTypeA, "Flota Andina S.A.S.", "Matrícula inicial");
            SeedCatalog(seed, orphanClient, ProcedureTypeA, "Rodar Leasing S.A.", "Matrícula inicial");
            SeedDelivered(seed, Guid.NewGuid(), grantedClient, TransitOffice, ProcedureTypeA, "REF-G1");
            SeedDelivered(seed, Guid.NewGuid(), grantedClient, TransitOffice, ProcedureTypeA, "REF-G2");
            SeedDelivered(seed, Guid.NewGuid(), orphanClient, TransitOffice, ProcedureTypeA, "REF-O1");
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        var health = await new GetOtBandejaHealthHandler(repo).HandleAsync(
            new GetOtBandejaHealthQuery { OtTenantId = OtTenant },
            TestContext.Current.CancellationToken);

        health.DeliveredTotal.Should().Be(3);
        health.DeliveredWithGrant.Should().Be(2);
        health.DeliveredWithoutGrant.Should().Be(1);
    }

    private static void SeedOt(FlitDbContext ctx, Guid otTenantId, Guid transitOfficeId)
    {
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = otTenantId,
            TransitOfficeId = transitOfficeId,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedGrant(FlitDbContext ctx, Guid clientTenantId, Guid transitOfficeId, bool isEnabled)
    {
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = clientTenantId,
            TransitOfficeId = transitOfficeId,
            IsEnabled = isEnabled,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedCatalog(
        FlitDbContext ctx,
        Guid clientTenantId,
        Guid procedureTypeId,
        string tenantLegalName,
        string procedureTypeName)
    {
        if (!ctx.Tenants.Any(t => t.Id == clientTenantId))
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = clientTenantId,
                Code = "client",
                LegalName = tenantLegalName,
                TaxId = "900000000",
                TenantType = "client",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        if (!ctx.ProcedureTypes.Local.Any(pt => pt.Id == procedureTypeId) && !ctx.ProcedureTypes.Any(pt => pt.Id == procedureTypeId))
        {
            ctx.ProcedureTypes.Add(new ProcedureType
            {
                Id = procedureTypeId,
                Code = "matricula_inicial",
                Name = procedureTypeName,
                Family = "MATRICULAS",
                IsActive = true,
                PublicationStatus = PublicationStatus.Published,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        ctx.SaveChanges();
    }

    private static void SeedDelivered(
        FlitDbContext ctx,
        Guid id,
        Guid clientTenantId,
        Guid transitOfficeId,
        Guid procedureTypeId,
        string reference = "REF-001")
    {
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = clientTenantId,
            ProcedureTypeId = procedureTypeId,
            ReferenceNumber = reference,
            Status = TramiteEstado.Entregado,
            TransitOfficeId = transitOfficeId,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}

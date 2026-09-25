using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.TransitOffices.OtPrendaDocumentPolicy;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// Bug #12912 (commit 3) — la política de prenda por OT se configura para toda compañía que puede
/// radicar en el organismo según la lista efectiva de red (HU #12347). La política es PROPIA de cada
/// compañía (decisión del usuario): la de la hija no hereda de la cabeza ni la cabeza de la hija.
/// PostgreSQL real (arnés HU #12319).
/// <para>Uso de ejemplo:
/// <c>new OtPrendaDocumentPolicyRepository(ctx, audit, resolver).ListCompaniesForOfficeAsync(ot)</c>
/// incluye a las hijas de la Concesión y a la red Marca Blanca con su propio flag.</para>
/// </summary>
public sealed class EffectiveNetworkPrendaPolicyIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static EffectiveTransitOfficeListResolver NewResolver(FlitDbContext ctx) =>
        new(
            new CompanyHierarchyRepository(ctx),
            new TransitGrantRepository(ctx, NullAuditContextAccessor.Instance),
            new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance),
            new DbTransitOfficeOperationalStatusReader(ctx),
            NullLogger<EffectiveTransitOfficeListResolver>.Instance);

    private static OtPrendaDocumentPolicyRepository NewRepository(FlitDbContext ctx) =>
        new(ctx, NullAuditContextAccessor.Instance, NewResolver(ctx));

    private static SetOtPrendaDocumentPolicyHandler NewSetHandler(FlitDbContext ctx)
    {
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.Exists(Arg.Any<Guid>()).Returns(true);
        return new SetOtPrendaDocumentPolicyHandler(catalog, NewResolver(ctx), NewRepository(ctx));
    }

    private static SetOtPrendaDocumentPolicyCommand Opcional(Guid tenantId) => new()
    {
        TenantId = tenantId,
        TransitOfficeId = HierarchyScenario.Ot1,
        DocumentOptional = true,
    };

    private static DateTimeOffset Ahora => DateTimeOffset.UtcNow.AddMinutes(1);

    /// <summary>Concesión P con grant a Ot1 (C1/C2 heredan) y red Marca Blanca sin bloqueos.</summary>
    private async Task SeedRedAsync()
    {
        await TransitNetworkSeed.SeedMarcaBlancaNetworkAsync(Fixture);
        await using var ctx = NewContext();
        await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot1);
    }

    private async Task<SetOtPrendaDocumentPolicyResult> GuardarAsync(Guid tenantId)
    {
        await using var ctx = NewContext();
        return await NewSetHandler(ctx).HandleAsync(Opcional(tenantId));
    }

    private async Task<bool> EsOpcionalAsync(Guid tenantId)
    {
        await using var ctx = NewContext();
        return await NewRepository(ctx).IsDocumentOptionalAtAsync(tenantId, HierarchyScenario.Ot1, Ahora);
    }

    [PostgresFact]
    public async Task ListCompaniesForOffice_incluye_hijas_de_Concesion_y_red_Marca_Blanca_con_su_flag()
    {
        await SeedRedAsync();
        await using (var seed = NewContext())
        {
            var repo = NewRepository(seed);
            await repo.SetAsync(HierarchyScenario.C2, HierarchyScenario.Ot1, true, null, null);
            await repo.SetAsync(TransitNetworkSeed.MbC1, HierarchyScenario.Ot1, true, null, null);
        }

        await using var ctx = NewContext();
        var companies = await NewRepository(ctx).ListCompaniesForOfficeAsync(HierarchyScenario.Ot1);

        companies.Select(c => c.TenantId).Should().BeEquivalentTo(
        [
            HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2,
            TransitNetworkSeed.MbHead, TransitNetworkSeed.MbC1, TransitNetworkSeed.MbC2,
        ]);
        companies.Where(c => c.DocumentOptional).Select(c => c.TenantId)
            .Should().BeEquivalentTo([HierarchyScenario.C2, TransitNetworkSeed.MbC1]);
        companies.Should().NotContain(c => c.TenantId == HierarchyScenario.X);
    }

    [PostgresFact]
    public async Task Guardar_politica_desde_la_red_es_propia_de_cada_compania()
    {
        await SeedRedAsync();

        (await GuardarAsync(HierarchyScenario.C2)).IsValid.Should().BeTrue();
        (await GuardarAsync(TransitNetworkSeed.MbC1)).IsValid.Should().BeTrue();

        (await EsOpcionalAsync(HierarchyScenario.C2)).Should().BeTrue();
        (await EsOpcionalAsync(TransitNetworkSeed.MbC1)).Should().BeTrue();
        (await EsOpcionalAsync(HierarchyScenario.P)).Should().BeFalse("la cabeza no hereda la política de su hija");
        (await EsOpcionalAsync(TransitNetworkSeed.MbHead)).Should().BeFalse("la cabeza MB no hereda la de su hija");

        (await GuardarAsync(TransitNetworkSeed.MbHead)).IsValid.Should().BeTrue();

        (await EsOpcionalAsync(TransitNetworkSeed.MbHead)).Should().BeTrue();
        (await EsOpcionalAsync(TransitNetworkSeed.MbC2)).Should().BeFalse("la hija no hereda la política de la cabeza");
        (await EsOpcionalAsync(HierarchyScenario.C1)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task Guardar_politica_en_OT_bloqueado_para_Marca_Blanca_es_error_de_transitOfficeId()
    {
        await SeedRedAsync();
        await using (var ctx = NewContext())
        {
            await new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance)
                .AddBlockAsync(TransitNetworkSeed.MbHead, TransitNetworkSeed.Ot1, null, null);
        }

        var result = await GuardarAsync(TransitNetworkSeed.MbC1);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.Field == "transitOfficeId"
            && e.Message == SetOtPrendaDocumentPolicyHandler.TransitOfficeNotGrantedMessage
            && e.Value == TransitNetworkSeed.Ot1.ToString());
        (await EsOpcionalAsync(TransitNetworkSeed.MbC1)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task Guardar_politica_de_compania_fuera_de_red_sin_grant_sigue_rechazada()
    {
        await SeedRedAsync();

        // X no pertenece a ninguna red y su grant es a Ot2.
        var result = await GuardarAsync(HierarchyScenario.X);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.Field == "transitOfficeId"
            && e.Message == SetOtPrendaDocumentPolicyHandler.TransitOfficeNotGrantedMessage);
    }
}

using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Application.Ocr;
using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// Bug #12912 (Entrega 1) — los mandatarios leen la lista EFECTIVA de OT de la red (HU #12347), no el
/// grant propio: la hija de una Concesión ve los OT de la cabeza y la red Marca Blanca ve todos los
/// operables menos los bloqueados por su cabeza. PostgreSQL real (arnés HU #12319).
/// <para>Uso de ejemplo:
/// <c>new DbMandateSignerReader(ctx, effectiveOffices: resolver).ListCompanyTransitOfficesAsync(hija)</c>
/// devuelve los organismos de la cabeza Concesión.</para>
/// </summary>
public sealed class EffectiveNetworkMandatesIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static EffectiveTransitOfficeListResolver NewResolver(FlitDbContext ctx) =>
        new(
            new CompanyHierarchyRepository(ctx),
            new TransitGrantRepository(ctx, NullAuditContextAccessor.Instance),
            new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance),
            new DbTransitOfficeOperationalStatusReader(ctx),
            NullLogger<EffectiveTransitOfficeListResolver>.Instance);

    private static DbMandateSignerReader NewReader(FlitDbContext ctx) =>
        new(ctx, effectiveOffices: NewResolver(ctx));

    private static CreateCompanyMandateSignerHandler NewCreateHandler(FlitDbContext ctx)
    {
        var reader = NewReader(ctx);
        var inner = new CreateMandateSignerHandler(
            new DbTransitOfficeOperationalStatusReader(ctx), reader, new MandateSignerRepository(ctx));
        return new CreateCompanyMandateSignerHandler(reader, inner);
    }

    private static MandateConfigAdminService NewConfigService(FlitDbContext ctx)
    {
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.GetById(HierarchyScenario.Ot1)
            .Returns(new TransitOfficeEntry(HierarchyScenario.Ot1, "11001000", "BOGOTA", "11", "11001"));
        return new MandateConfigAdminService(
            ctx,
            catalog,
            new DbTransitOfficeOperationalStatusReader(ctx),
            Substitute.For<IDocumentOcrAnalyzer>(),
            Substitute.For<IMandateTemplateStorage>(),
            NewResolver(ctx));
    }

    private static CompanyMandateSignerRequest Alta(string documento, params Guid[] organismos) =>
        new("Ana Restrepo", documento, organismos, "CC", "ana@flit.test");

    /// <summary>Concesión P con grant a Ot1 (C1/C2 heredan) y red Marca Blanca sin bloqueos.</summary>
    private async Task SeedRedAsync()
    {
        await TransitNetworkSeed.SeedMarcaBlancaNetworkAsync(Fixture);
        await using var ctx = NewContext();
        await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot1);
    }

    private async Task BloquearOt1ParaMarcaBlancaAsync()
    {
        await using var ctx = NewContext();
        await new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance)
            .AddBlockAsync(TransitNetworkSeed.MbHead, TransitNetworkSeed.Ot1, null, null);
    }

    // (a) hija de Concesión
    [PostgresFact]
    public async Task ListCompanyTransitOffices_hija_de_Concesion_lista_los_OT_de_la_cabeza()
    {
        await SeedRedAsync();
        await using var ctx = NewContext();

        // C2 tiene grant propio SOLO a Ot2; la cabeza P tiene Ot1 ⇒ la lista efectiva es Ot1.
        var options = await NewReader(ctx).ListCompanyTransitOfficesAsync(HierarchyScenario.C2);

        options.Select(o => o.TransitOfficeId).Should().Equal([HierarchyScenario.Ot1]);
    }

    // (b) cabeza e hija Marca Blanca, antes y después del bloqueo
    [PostgresFact]
    public async Task ListCompanyTransitOffices_Marca_Blanca_lista_operables_menos_bloqueados()
    {
        await SeedRedAsync();

        await using (var ctx = NewContext())
        {
            var reader = NewReader(ctx);
            (await reader.ListCompanyTransitOfficesAsync(TransitNetworkSeed.MbHead))
                .Select(o => o.TransitOfficeId).Should().Equal([TransitNetworkSeed.Ot1]);
            (await reader.ListCompanyTransitOfficesAsync(TransitNetworkSeed.MbC1))
                .Select(o => o.TransitOfficeId).Should().Equal([TransitNetworkSeed.Ot1]);
        }

        await BloquearOt1ParaMarcaBlancaAsync();

        await using var after = NewContext();
        var afterReader = NewReader(after);
        (await afterReader.ListCompanyTransitOfficesAsync(TransitNetworkSeed.MbHead)).Should().BeEmpty();
        (await afterReader.ListCompanyTransitOfficesAsync(TransitNetworkSeed.MbC1)).Should().BeEmpty();
    }

    // (c) alta desde la hija de Concesión y desde la hija MB
    [PostgresFact]
    public async Task Alta_de_mandatario_desde_hija_de_Concesion_y_de_Marca_Blanca_es_valida()
    {
        await SeedRedAsync();

        await using (var ctx = NewContext())
        {
            var result = await NewCreateHandler(ctx)
                .HandleAsync(HierarchyScenario.C2, Alta("1020304050", HierarchyScenario.Ot1), null);
            result.Errors.Should().BeEmpty();
            result.IsValid.Should().BeTrue();
        }

        await using var mbCtx = NewContext();
        var mb = await NewCreateHandler(mbCtx)
            .HandleAsync(TransitNetworkSeed.MbC1, Alta("1020304051", TransitNetworkSeed.Ot1), null);
        mb.Errors.Should().BeEmpty();
        mb.IsValid.Should().BeTrue();
    }

    // (c) OT bloqueado para la cabeza MB ⇒ 422 en transitOfficeIds
    [PostgresFact]
    public async Task Alta_de_mandatario_en_OT_bloqueado_para_Marca_Blanca_es_422()
    {
        await SeedRedAsync();
        await BloquearOt1ParaMarcaBlancaAsync();

        await using var ctx = NewContext();
        var result = await NewCreateHandler(ctx)
            .HandleAsync(TransitNetworkSeed.MbC1, Alta("1020304052", TransitNetworkSeed.Ot1), null);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.Field == "transitOfficeIds" && e.Value == TransitNetworkSeed.Ot1.ToString());
    }

    // (d) reglas por compañía del OT
    [PostgresFact]
    public async Task ListCompanyRules_incluye_hijas_de_Concesion_y_red_Marca_Blanca()
    {
        await SeedRedAsync();
        await using var ctx = NewContext();

        var rules = await NewConfigService(ctx).ListCompanyRulesAsync(HierarchyScenario.Ot1);

        rules.Select(r => r.CompanyTenantId).Should().BeEquivalentTo(
        [
            HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2,
            TransitNetworkSeed.MbHead, TransitNetworkSeed.MbC1, TransitNetworkSeed.MbC2,
        ]);
    }

    // (e) guardar regla / mandatario por defecto para una hija
    [PostgresFact]
    public async Task Guardar_regla_y_default_para_hija_no_da_CompanyNotFound()
    {
        await SeedRedAsync();

        await using (var ctx = NewContext())
        {
            var (status, view) = await NewConfigService(ctx).UpsertCompanyRuleAsync(
                HierarchyScenario.Ot1,
                HierarchyScenario.C2,
                new UpsertCompanyOtMandateRuleRequest(MandatoAssignmentModeCodes.Open),
                null);
            status.Should().Be(MandateConfigWriteStatus.Ok);
            view!.CompanyTenantId.Should().Be(HierarchyScenario.C2);
        }

        await using (var ctx = NewContext())
        {
            var (status, _) = await NewConfigService(ctx).SetCompanyDefaultSignerAsync(
                HierarchyScenario.Ot1,
                TransitNetworkSeed.MbC2,
                new SetCompanyDefaultSignerRequest(null),
                null);
            status.Should().Be(MandateConfigWriteStatus.Ok);
        }

        // X no pertenece a ninguna red y su grant es a Ot2: sigue siendo CompanyNotFound.
        await using var outsider = NewContext();
        var (outsiderStatus, _) = await NewConfigService(outsider).SetCompanyDefaultSignerAsync(
            HierarchyScenario.Ot1,
            HierarchyScenario.X,
            new SetCompanyDefaultSignerRequest(null),
            null);
        outsiderStatus.Should().Be(MandateConfigWriteStatus.CompanyNotFound);
    }

    // (f) consola de mandatarios del OT
    [PostgresFact]
    public async Task ListOtCompanies_incluye_la_red_como_habilitada()
    {
        await SeedRedAsync();
        await using var ctx = NewContext();

        var companies = await NewReader(ctx).ListOtCompaniesAsync(HierarchyScenario.Ot1);

        companies.Where(c => c.IsEnabled).Select(c => c.CompanyTenantId).Should().BeEquivalentTo(
        [
            HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2,
            TransitNetworkSeed.MbHead, TransitNetworkSeed.MbC1, TransitNetworkSeed.MbC2,
        ]);
        companies.Should().NotContain(c => c.CompanyTenantId == HierarchyScenario.X);
    }
}

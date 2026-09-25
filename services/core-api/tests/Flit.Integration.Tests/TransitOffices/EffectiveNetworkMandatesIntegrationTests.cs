using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
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

        var rules = await NewConfigService(ctx).ListCompanyRulesAsync(HierarchyScenario.Ot1, OtCompanyVisibility.WholeNetwork);

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
                null,
                OtCompanyVisibility.WholeNetwork);
            status.Should().Be(MandateConfigWriteStatus.Ok);
        }

        // X no pertenece a ninguna red y su grant es a Ot2: sigue siendo CompanyNotFound.
        await using var outsider = NewContext();
        var (outsiderStatus, _) = await NewConfigService(outsider).SetCompanyDefaultSignerAsync(
            HierarchyScenario.Ot1,
            HierarchyScenario.X,
            new SetCompanyDefaultSignerRequest(null),
            null,
            OtCompanyVisibility.WholeNetwork);
        outsiderStatus.Should().Be(MandateConfigWriteStatus.CompanyNotFound);
    }

    // (f) consola de mandatarios del OT
    [PostgresFact]
    public async Task ListOtCompanies_incluye_la_red_como_habilitada()
    {
        await SeedRedAsync();
        await using var ctx = NewContext();

        var companies = await NewReader(ctx).ListOtCompaniesAsync(HierarchyScenario.Ot1, OtCompanyVisibility.WholeNetwork);

        companies.Where(c => c.IsEnabled).Select(c => c.CompanyTenantId).Should().BeEquivalentTo(
        [
            HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2,
            TransitNetworkSeed.MbHead, TransitNetworkSeed.MbC1, TransitNetworkSeed.MbC2,
        ]);
        companies.Should().NotContain(c => c.CompanyTenantId == HierarchyScenario.X);
    }

    // Bug #12912 (review PR #442) - Ley 1581 en la vista del organismo.

    private const OtCompanyVisibility VistaOt = OtCompanyVisibility.DirectOrWithReceivedProcedures;

    [PostgresFact]
    public async Task Ley1581_ListCompanyRules_vista_OT_solo_red_con_tramites_recibidos()
    {
        await SeedRedAsync();
        await using var ctx = NewContext();

        var rules = await NewConfigService(ctx).ListCompanyRulesAsync(HierarchyScenario.Ot1, VistaOt);

        // P (grant directo) y C1/C2 (entregaron a Ot1) sí; la red Marca Blanca no ha radicado.
        rules.Select(r => r.CompanyTenantId).Should().BeEquivalentTo(
            [HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2]);
    }

    [PostgresFact]
    public async Task Ley1581_ListOtCompanies_vista_OT_solo_red_con_tramites_recibidos()
    {
        await SeedRedAsync();
        await using var ctx = NewContext();

        var companies = await NewReader(ctx).ListOtCompaniesAsync(HierarchyScenario.Ot1, VistaOt);

        companies.Select(c => c.CompanyTenantId).Should().BeEquivalentTo(
            [HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2]);
        companies.Should().OnlyContain(c => c.IsEnabled);
    }

    [PostgresFact]
    public async Task Ley1581_escrituras_vista_OT_sobre_compania_no_visible_son_CompanyNotFound()
    {
        await SeedRedAsync();

        await using (var ctx = NewContext())
        {
            var (status, _) = await NewConfigService(ctx).SetCompanyDefaultSignerAsync(
                HierarchyScenario.Ot1, TransitNetworkSeed.MbC1, new SetCompanyDefaultSignerRequest(null), null,
                VistaOt);
            status.Should().Be(MandateConfigWriteStatus.CompanyNotFound);
        }

        await using (var ctx = NewContext())
        {
            var status = await NewConfigService(ctx).DeleteCompanyRuleAsync(
                HierarchyScenario.Ot1, TransitNetworkSeed.MbC1, VistaOt);
            status.Should().Be(MandateConfigWriteStatus.CompanyNotFound);
        }

        // Visible (entró por la red y entregó trámites) => se puede escribir.
        await using (var ctx = NewContext())
        {
            var (status, _) = await NewConfigService(ctx).SetCompanyDefaultSignerAsync(
                HierarchyScenario.Ot1, HierarchyScenario.C2, new SetCompanyDefaultSignerRequest(null), null,
                VistaOt);
            status.Should().Be(MandateConfigWriteStatus.Ok);
        }

        // SuperAdmin (toda la red) sí puede escribir para la compañía MB.
        await using var superAdmin = NewContext();
        var (superStatus, _) = await NewConfigService(superAdmin).SetCompanyDefaultSignerAsync(
            HierarchyScenario.Ot1, TransitNetworkSeed.MbC1, new SetCompanyDefaultSignerRequest(null), null,
            OtCompanyVisibility.WholeNetwork);
        superStatus.Should().Be(MandateConfigWriteStatus.Ok);
    }

    [PostgresFact]
    public async Task Ley1581_alta_de_mandatario_desde_el_OT_rechaza_compania_no_visible()
    {
        await SeedRedAsync();

        await using (var ctx = NewContext())
        {
            var invisible = await NewOtCreateHandler(ctx).HandleAsync(OtAlta("1020304060", TransitNetworkSeed.MbC1));
            invisible.IsValid.Should().BeFalse();
            invisible.Errors.Should().Contain(e =>
                e.Field == "companyTenantIds" && e.Value == TransitNetworkSeed.MbC1.ToString());
        }

        await using var visibleCtx = NewContext();
        var visible = await NewOtCreateHandler(visibleCtx).HandleAsync(OtAlta("1020304061", HierarchyScenario.C2));
        visible.Errors.Should().BeEmpty();
    }

    private static CreateMandateSignerHandler NewOtCreateHandler(FlitDbContext ctx) =>
        new(new DbTransitOfficeOperationalStatusReader(ctx), NewReader(ctx), new MandateSignerRepository(ctx));

    private static CreateMandateSignerCommand OtAlta(string documento, Guid company) => new()
    {
        TransitOfficeId = HierarchyScenario.Ot1,
        FullName = "Ana Restrepo",
        DocumentNumber = documento,
        CompanyTenantIds = [company],
        Email = "ana@flit.test",
        CompanyVisibility = VistaOt,
    };

    // Bug #12912 (2ª vuelta review PR #442) - edición conservadora y recorte de PII en la vista OT.

    /// <summary>
    /// Alta con toda la red (como la haría Plataforma) con primario Ot1, y el mandatario además
    /// vinculado a Ot2 con firma a mano (fila del puente sembrada: las compañías de la semilla no están
    /// habilitadas en Ot2, así que RF33 no dejaría darla de alta por el handler).
    /// </summary>
    private async Task<Guid> AltaRedAsync(string documento, params Guid[] companies)
    {
        Guid signerId;
        await using (var ctx = NewContext())
        {
            var result = await NewOtCreateHandler(ctx).HandleAsync(new CreateMandateSignerCommand
            {
                TransitOfficeId = HierarchyScenario.Ot1,
                FullName = "Mandatario " + documento,
                DocumentNumber = documento,
                CompanyTenantIds = companies,
                Email = "mandatario@flit.test",
                CompanyVisibility = OtCompanyVisibility.WholeNetwork,
            });
            result.Errors.Should().BeEmpty();
            signerId = result.MandateSignerId!.Value;
        }

        await using var seed = NewContext();
        seed.MandateSignerTransitOffices.Add(new Flit.Infrastructure.Persistence.Entities.Admin.MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(),
            MandateSignerId = signerId,
            TransitOfficeId = HierarchyScenario.Ot2,
            IsActive = true,
            SignsPhysically = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await seed.SaveChangesAsync();
        return signerId;
    }

    private async Task<UpdateMandateSignerResult> EdicionOtAsync(Guid signerId, string email, params Guid[] companies)
    {
        await using var ctx = NewContext();
        var handler = new UpdateMandateSignerHandler(
            new DbTransitOfficeOperationalStatusReader(ctx), NewReader(ctx), new MandateSignerRepository(ctx));
        return await handler.HandleAsync(new UpdateMandateSignerCommand
        {
            TransitOfficeId = HierarchyScenario.Ot1,
            MandateSignerId = signerId,
            FullName = "Mandatario editado",
            DocumentNumber = "2000000002",
            CompanyTenantIds = companies,
            Email = email,
            // El frontend del OT solo conoce su propio organismo (la lista le llega recortada).
            TransitOfficeIds = [HierarchyScenario.Ot1],
            CompanyVisibility = VistaOt,
        });
    }

    private async Task<MandateSignerItem> LeerAsync(Guid signerId)
    {
        await using var ctx = NewContext();
        return (await NewReader(ctx).GetByIdAsync(signerId))!;
    }

    [PostgresFact]
    public async Task HabeasData_ListByOt_vista_OT_omite_mandatarios_ajenos_y_recorta_companias_y_organismos()
    {
        await SeedRedAsync();
        var soloRed = await AltaRedAsync("2000000001", TransitNetworkSeed.MbC1);
        var mixto = await AltaRedAsync("2000000002", HierarchyScenario.C2, TransitNetworkSeed.MbC2);

        await using var ctx = NewContext();
        var vistaOt = await NewReader(ctx).ListByOtAsync(HierarchyScenario.Ot1, VistaOt);

        vistaOt.Select(s => s.Id).Should().Equal([mixto], "el mandatario solo de la red MB no le compete al organismo");
        var visible = vistaOt.Single();
        visible.CompanyTenantIds.Should().Equal([HierarchyScenario.C2]);
        visible.TransitOfficeIds.Should().Equal([HierarchyScenario.Ot1]);
        visible.PhysicalSignatureOfficeIds.Should().BeEmpty();

        var plataforma = await NewReader(ctx).ListByOtAsync(HierarchyScenario.Ot1, OtCompanyVisibility.WholeNetwork);
        plataforma.Select(s => s.Id).Should().BeEquivalentTo([soloRed, mixto]);
        var completo = plataforma.Single(s => s.Id == mixto);
        completo.CompanyTenantIds.Distinct().Should().BeEquivalentTo([HierarchyScenario.C2, TransitNetworkSeed.MbC2]);
        completo.TransitOfficeIds.Should().BeEquivalentTo([HierarchyScenario.Ot1, HierarchyScenario.Ot2]);
        completo.PhysicalSignatureOfficeIds.Should().Equal([HierarchyScenario.Ot2]);
    }

    [PostgresFact]
    public async Task Edicion_desde_el_OT_conserva_companias_no_visibles_y_organismos_ajenos()
    {
        await SeedRedAsync();
        var signer = await AltaRedAsync("2000000002", HierarchyScenario.C2, TransitNetworkSeed.MbC2);

        // El OT reenvía la lista que recibió (recortada a C2) y cambia el correo.
        var recortada = await EdicionOtAsync(signer, "nuevo@flit.test", HierarchyScenario.C2);
        recortada.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);

        var tras = await LeerAsync(signer);
        tras.Email.Should().Be("nuevo@flit.test");
        tras.CompanyTenantIds.Distinct().Should().BeEquivalentTo([HierarchyScenario.C2, TransitNetworkSeed.MbC2]);
        tras.TransitOfficeIds.Should().BeEquivalentTo([HierarchyScenario.Ot1, HierarchyScenario.Ot2]);
        tras.PhysicalSignatureOfficeIds.Should().Equal([HierarchyScenario.Ot2]);

        // Reenviar la lista completa (incluida la no visible, ya asignada) tampoco es 422.
        var completa = await EdicionOtAsync(signer, "otro@flit.test", HierarchyScenario.C2, TransitNetworkSeed.MbC2);
        completa.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);

        // Lo que el OT AGREGA sí se valida: una compañía de la red que no le es visible es 422.
        var agrega = await EdicionOtAsync(signer, "otro@flit.test", HierarchyScenario.C2, TransitNetworkSeed.MbC1);
        agrega.Outcome.Should().NotBe(UpdateMandateSignerOutcome.Updated);
        agrega.Errors.Should().Contain(e =>
            e.Field == "companyTenantIds" && e.Value == TransitNetworkSeed.MbC1.ToString());
    }

    [PostgresFact]
    public async Task Ley1581_ListOtCompanies_vista_OT_no_habilita_grant_directo_inhabilitado_de_la_red()
    {
        await SeedRedAsync();
        await using (var seed = NewContext())
        {
            // MbC1 puede radicar en Ot1 por la red MB, no ha entregado trámites y tiene un grant
            // directo INHABILITADO a Ot1: no es visible y no debe salir como habilitada.
            seed.TenantTransitOfficeGrants.Add(new Flit.Infrastructure.Persistence.Entities.Admin.TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(),
                TenantId = TransitNetworkSeed.MbC1,
                TransitOfficeId = HierarchyScenario.Ot1,
                IsEnabled = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext();
        var companies = await NewReader(ctx).ListOtCompaniesAsync(HierarchyScenario.Ot1, VistaOt);

        companies.Should().NotContain(c => c.CompanyTenantId == TransitNetworkSeed.MbC1);
        (await NewConfigService(ctx).ListCompanyRulesAsync(HierarchyScenario.Ot1, VistaOt))
            .Should().NotContain(r => r.CompanyTenantId == TransitNetworkSeed.MbC1);
    }
}

using System.Text.Json;
using Flit.Admin.Application.Companies.MandateSigners.DeleteMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.GetMandateSignerImpact;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Admin.Tests.Companies.MandateSigners.MandateSignerLifecycleKit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>HU #13136 (Feature #13115) — reactivar restaurando vínculos sin desplazar al default vigente, sobre InMemory.</summary>
public sealed class MandateSignerReactivateTests
{
    [Fact]
    public async Task HU13136_AC1_restaura_los_vinculos_de_dos_organismos_y_tres_companias()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [
            (Office, CompanyA, "organismo"), (Office, CompanyB, "organismo"), (Office2, CompanyC, "organismo")]);
        await sut.Inactivate.HandleAsync(Off(signer), Ct);
        ctx.MandateSignerCompanies.Where(c => c.MandateSignerId == signer).Should().OnlyContain(c => !c.IsActive);

        var result = await sut.Reactivate.HandleDetailedAsync(On(signer), Ct);

        result.Outcome.Should().Be(ReactivateMandateSignerOutcome.Reactivated);
        result.Lifecycle!.RestoredLinks.Should().HaveCount(3);
        result.Lifecycle.ConflictLinks.Should().BeEmpty();
        ctx.MandateSignerCompanies.Where(c => c.MandateSignerId == signer).Should().OnlyContain(c => c.IsActive);
        ctx.MandateSignerTransitOffices.Where(o => o.MandateSignerId == signer).Select(o => o.TransitOfficeId)
            .Should().BeEquivalentTo([Office, Office2]);
        ctx.MandateSignerTransitOffices.Where(o => o.MandateSignerId == signer).Should().OnlyContain(o => o.IsActive);
        ctx.MandateSigners.Single(s => s.Id == signer).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task HU13136_solo_restaura_lo_que_retiro_la_baja_no_lo_que_quito_una_edicion_previa()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);
        // Un vínculo ya inactivo por una edición anterior (B): la baja no lo retiró, así que no vuelve.
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(), MandateSignerId = signer, TransitOfficeId = Office, CompanyTenantId = CompanyB,
            IsActive = false, ConfiguredByScope = "organismo", CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
        await sut.Inactivate.HandleAsync(Off(signer), Ct);

        await sut.Reactivate.HandleAsync(On(signer), Ct);

        ctx.MandateSignerCompanies.Single(c => c.MandateSignerId == signer && c.CompanyTenantId == CompanyA).IsActive.Should().BeTrue();
        ctx.MandateSignerCompanies.Single(c => c.MandateSignerId == signer && c.CompanyTenantId == CompanyB).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task HU13136_AC2_no_desplaza_al_default_que_quedo_tras_la_baja()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var anterior = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);
        SetRuleDefault(ctx, Office, CompanyA, anterior);
        await sut.Inactivate.HandleAsync(Off(anterior), Ct);
        var otro = AddSigner(ctx, "Beto", [(Office, CompanyA, "compania")]);
        SetRuleDefault(ctx, Office, CompanyA, otro);   // quedó como default tras la baja

        var result = await sut.Reactivate.HandleDetailedAsync(On(anterior), Ct);

        result.Outcome.Should().Be(ReactivateMandateSignerOutcome.Reactivated);
        ctx.CompanyOtMandateRules.Single().DefaultMandateSignerId.Should().Be(otro);
        result.Lifecycle!.RestoredDefaults.Should().Be(0);
    }

    [Fact]
    public async Task HU13136_AC3_recupera_el_default_que_quedo_vacio()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyB, "organismo")]);
        SetRuleDefault(ctx, Office, CompanyB, signer);
        SetOfficeDefault(ctx, Office, signer);
        await sut.Inactivate.HandleAsync(Off(signer), Ct);
        ctx.CompanyOtMandateRules.Single().DefaultMandateSignerId.Should().BeNull();

        var result = await sut.Reactivate.HandleDetailedAsync(On(signer), Ct);

        result.Lifecycle!.RestoredDefaults.Should().Be(2);
        ctx.CompanyOtMandateRules.Single().DefaultMandateSignerId.Should().Be(signer);
        ctx.TransitOfficeMandateConfigs.Single().DefaultMandateSignerId.Should().Be(signer);
    }

    [Fact]
    public async Task HU13136_AC4_el_vinculo_en_conflicto_se_restaura_inactivo_y_la_respuesta_lo_informa()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo"), (Office, CompanyC, "organismo")]);
        await sut.Inactivate.HandleAsync(Off(signer), Ct);
        // Mientras tanto otro mandatario del MISMO origen tomó a la compañía C en el organismo 1.
        AddSigner(ctx, "Beto", [(Office, CompanyC, "super_admin")]);

        var result = await sut.Reactivate.HandleDetailedAsync(On(signer), Ct);

        result.Outcome.Should().Be(ReactivateMandateSignerOutcome.Reactivated);
        result.Lifecycle!.RestoredLinks.Should().ContainSingle().Which.CompanyTenantId.Should().Be(CompanyA);
        result.Lifecycle.ConflictLinks.Should().ContainSingle().Which.CompanyTenantId.Should().Be(CompanyC);
        ctx.MandateSignerCompanies.Single(c => c.MandateSignerId == signer && c.CompanyTenantId == CompanyC).IsActive
            .Should().BeFalse("el vínculo en conflicto queda inactivo");
        ctx.MandateSignerCompanies.Single(c => c.MandateSignerId == signer && c.CompanyTenantId == CompanyA).IsActive
            .Should().BeTrue();
    }

    [Fact]
    public async Task HU13136_un_activo_de_otro_origen_no_es_conflicto()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);
        await sut.Inactivate.HandleAsync(Off(signer), Ct);
        AddSigner(ctx, "Beto", [(Office, CompanyA, "compania")]);   // otro grupo de origen: conviven

        var result = await sut.Reactivate.HandleDetailedAsync(On(signer), Ct);

        result.Lifecycle!.ConflictLinks.Should().BeEmpty();
        result.Lifecycle.RestoredLinks.Should().ContainSingle();
    }

    [Fact]
    public async Task HU13136_si_todos_los_vinculos_chocan_responde_conflicto_y_no_cambia_nada()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);
        await sut.Inactivate.HandleAsync(Off(signer), Ct);
        AddSigner(ctx, "Beto", [(Office, CompanyA, "organismo")]);
        var eventos = Lifecycle(ctx).Count;

        var result = await sut.Reactivate.HandleDetailedAsync(On(signer), Ct);

        result.Outcome.Should().Be(ReactivateMandateSignerOutcome.Conflict);
        result.Lifecycle!.ConflictLinks.Should().ContainSingle();
        ctx.MandateSigners.Single(s => s.Id == signer).IsActive.Should().BeFalse();
        Lifecycle(ctx).Should().HaveCount(eventos);
    }

    [Fact]
    public async Task HU13136_AC5_un_mandatario_eliminado_no_se_reactiva()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);
        await sut.Delete.HandleAsync(Del(signer, confirm: true), Ct);

        var result = await sut.Reactivate.HandleAsync(On(signer), Ct);

        result.Should().Be(ReactivateMandateSignerOutcome.NotFound);
    }

    [Fact]
    public async Task HU13136_baja_anterior_a_la_traza_conserva_el_comportamiento_previo()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        // Mandatario inactivado antes de existir la traza: sin evento «deactivated» en la bitácora.
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")], active: false);

        var result = await sut.Reactivate.HandleDetailedAsync(On(signer), Ct);

        result.Outcome.Should().Be(ReactivateMandateSignerOutcome.Reactivated);
        result.Lifecycle!.RestoredFromSnapshot.Should().BeFalse();
        ctx.MandateSigners.Single(s => s.Id == signer).IsActive.Should().BeTrue();
        ctx.MandateSignerCompanies.Single().IsActive.Should().BeFalse("sin traza no se sabe qué retiró la baja");
        ctx.MandateSignerTransitOffices.Single(o => o.TransitOfficeId == Office).IsActive.Should().BeTrue();
    }
}

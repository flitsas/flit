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

/// <summary>HU #13135 (Feature #13115) — eliminar (baja lógica), retiro de defaults, impacto y confirmación, sobre InMemory.</summary>
public sealed class MandateSignerDeleteTests
{
    [Fact]
    public async Task HU13135_AC1_eliminar_retira_todos_los_defaults_y_oculta_al_mandatario()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo"), (Office2, CompanyB, "organismo")]);
        SetRuleDefault(ctx, Office, CompanyA, signer);   // default de la compañía A en el organismo 1
        SetOfficeDefault(ctx, Office2, signer);          // default general del organismo 2

        var result = await sut.Delete.HandleAsync(Del(signer, confirm: true), Ct);

        result.Outcome.Should().Be(DeleteMandateSignerOutcome.Deleted);
        result.Lifecycle!.RetiredDefaults.Should().Be(2);
        ctx.CompanyOtMandateRules.Single().DefaultMandateSignerId.Should().BeNull();
        ctx.TransitOfficeMandateConfigs.Single().DefaultMandateSignerId.Should().BeNull();

        var row = ctx.MandateSigners.Single(s => s.Id == signer);
        row.DeletedAt.Should().NotBeNull();
        row.DeletedBy.Should().Be(Operator);
        row.IsActive.Should().BeFalse();
        ctx.MandateSignerCompanies.Where(c => c.MandateSignerId == signer).Should().OnlyContain(c => !c.IsActive);

        var list = await sut.List.HandleAsync(
            new ListMandateSignersQuery { TransitOfficeId = Office, Visibility = OtCompanyVisibility.WholeNetwork }, Ct);
        list.Should().NotContain(s => s.Id == signer, "el eliminado desaparece de todas las listas");
    }

    [Fact]
    public async Task HU13135_AC2_desactivar_tambien_retira_los_defaults()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);
        SetRuleDefault(ctx, Office, CompanyA, signer);
        SetOfficeDefault(ctx, Office, signer);

        var result = await sut.Inactivate.HandleDetailedAsync(Off(signer), Ct);

        result.Outcome.Should().Be(InactivateMandateSignerOutcome.Inactivated);
        result.Lifecycle!.RetiredDefaults.Should().Be(2);
        ctx.CompanyOtMandateRules.Single().DefaultMandateSignerId.Should().BeNull();
        ctx.TransitOfficeMandateConfigs.Single().DefaultMandateSignerId.Should().BeNull();
        ctx.MandateSigners.Single(s => s.Id == signer).DeletedAt.Should().BeNull("desactivar no es eliminar");
    }

    [Fact]
    public async Task HU13135_AC3_el_impacto_lista_unico_activo_defaults_y_tramites_pendientes()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo"), (Office, CompanyB, "organismo")]);
        // La compañía B tiene OTRO mandatario activo: en B Ana no es la única.
        AddSigner(ctx, "Beto", [(Office, CompanyB, "compania")]);
        SetRuleDefault(ctx, Office, CompanyA, signer);
        AddPending(ctx, signer);
        AddPending(ctx, signer, TramiteEstado.Borrador);   // borrador: no cuenta
        AddPending(ctx, signer, TramiteEstado.Aprobado);   // aprobado: no cuenta

        var impact = await sut.Impact.HandleAsync(Office, signer, Ct);

        impact.Should().NotBeNull();
        impact!.OnlyActiveFor.Should().ContainSingle()
            .Which.Should().Be(new MandateSignerLinkRef(Office, CompanyA));
        impact.Defaults.Should().ContainSingle()
            .Which.Should().Be(new MandateSignerDefaultRef(MandateSignerDefaultRef.CompanyRule, Office, CompanyA));
        impact.PendingProcedures.Should().Be(1);
        impact.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task HU13135_AC4_baja_con_impacto_sin_confirmar_no_cambia_ningun_dato()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var reassigner = new FakeReassigner(MandateSignerReassignmentResult.None);
        var sut = Build(ctx, reassigner);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);
        SetRuleDefault(ctx, Office, CompanyA, signer);

        var result = await sut.Delete.HandleAsync(Del(signer, confirm: false), Ct);

        result.Outcome.Should().Be(DeleteMandateSignerOutcome.ConfirmationRequired);
        result.Impact!.IsEmpty.Should().BeFalse();
        ctx.MandateSigners.Single(s => s.Id == signer).DeletedAt.Should().BeNull();
        ctx.MandateSigners.Single(s => s.Id == signer).IsActive.Should().BeTrue();
        ctx.CompanyOtMandateRules.Single().DefaultMandateSignerId.Should().Be(signer);
        ctx.MandateSignerCompanies.Single().IsActive.Should().BeTrue();
        reassigner.Calls.Should().Be(0);
        Lifecycle(ctx).Should().BeEmpty("una operación rechazada no deja registro");
    }

    [Fact]
    public async Task HU13135_AC5_baja_sin_impacto_no_exige_confirmacion()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);
        AddSigner(ctx, "Beto", [(Office, CompanyA, "compania")]);   // no es el único activo ni default de nadie

        var result = await sut.Delete.HandleAsync(Del(signer, confirm: false), Ct);

        result.Outcome.Should().Be(DeleteMandateSignerOutcome.Deleted);
    }

    [Fact]
    public async Task HU13135_AC6_historial_conservado_el_firmado_sigue_mostrando_quien_firmo()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);
        var firmado = AddPending(ctx, signer, TramiteEstado.Aprobado);

        (await sut.Delete.HandleAsync(Del(signer, confirm: true), Ct)).Outcome.Should().Be(DeleteMandateSignerOutcome.Deleted);

        // La fila y la referencia del trámite firmado siguen ahí (baja lógica, no borrado físico).
        ctx.MandateSigners.Should().ContainSingle(s => s.Id == signer);
        ctx.ProcedureInstances.Single(p => p.Id == firmado).MandateSignerId.Should().Be(signer);
    }

    [Fact]
    public async Task HU13135_AC8_doble_eliminacion_es_404_y_no_duplica_registros()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);

        (await sut.Delete.HandleAsync(Del(signer, confirm: true), Ct)).Outcome.Should().Be(DeleteMandateSignerOutcome.Deleted);
        var eventos = Lifecycle(ctx).Count;

        var again = await sut.Delete.HandleAsync(Del(signer, confirm: true), Ct);

        again.Outcome.Should().Be(DeleteMandateSignerOutcome.NotFound);
        Lifecycle(ctx).Should().HaveCount(eventos, "la segunda baja no deja más eventos");
    }
}

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

/// <summary>HU #13138 (Feature #13115) — bitácora de baja, eliminación, retiro de default, reasignación y reactivación.</summary>
public sealed class MandateSignerLifecycleAuditTests
{
    [Fact]
    public async Task HU13138_AC1_la_baja_registra_rol_modulo_mandatario_companias_y_organismos()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana Restrepo", [(Office, CompanyA, "organismo"), (Office2, CompanyB, "organismo")]);

        await sut.Inactivate.HandleAsync(Off(signer, MandateSignerActorKind.OtAdmin), Ct);

        var evento = Lifecycle(ctx).Single(l => l.FieldName == "deactivated");
        evento.Operation.Should().Be("deactivate");
        evento.Result.Should().Be("success");
        evento.TargetEntityType.Should().Be("MANDATE_SIGNER");
        evento.TargetEntityId.Should().Be(signer);
        evento.ChangedBy.Should().Be(Operator);

        using var doc = JsonDocument.Parse(evento.NewValue!);
        var root = doc.RootElement;
        root.GetProperty("actorRole").GetString().Should().Be("admin_ot");
        root.GetProperty("actorModule").GetString().Should().Be("ot");
        root.GetProperty("mandateSignerId").GetGuid().Should().Be(signer);
        root.GetProperty("links").GetArrayLength().Should().Be(2);
        root.GetProperty("offices").GetArrayLength().Should().Be(2);

        // Sin datos personales: ni el nombre ni el documento del mandatario.
        evento.NewValue.Should().NotContain("Ana").And.NotContain("Restrepo").And.NotContain("9988776655");
    }

    [Theory]
    [InlineData(MandateSignerActorKind.CompanyAdmin, "admin_compania", "compania")]
    [InlineData(MandateSignerActorKind.SuperAdmin, "super_admin", "plataforma")]
    public async Task HU13138_el_rol_y_el_modulo_siguen_a_quien_actua(
        MandateSignerActorKind actor, string rol, string modulo)
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "compania")]);

        await sut.Inactivate.HandleAsync(Off(signer, actor), Ct);

        using var doc = JsonDocument.Parse(Lifecycle(ctx).Single(l => l.FieldName == "deactivated").NewValue!);
        doc.RootElement.GetProperty("actorRole").GetString().Should().Be(rol);
        doc.RootElement.GetProperty("actorModule").GetString().Should().Be(modulo);
    }

    [Fact]
    public async Task HU13138_AC2_la_eliminacion_y_el_retiro_de_default_registran_un_evento_por_cada_default()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "compania"), (Office2, CompanyB, "compania")]);
        SetRuleDefault(ctx, Office, CompanyA, signer);
        SetRuleDefault(ctx, Office2, CompanyB, signer);
        SetOfficeDefault(ctx, Office, signer);

        await sut.Delete.HandleAsync(Del(signer, confirm: true, MandateSignerActorKind.CompanyAdmin), Ct);

        var eventos = Lifecycle(ctx);
        eventos.Should().ContainSingle(l => l.FieldName == "deleted" && l.Operation == "delete");
        eventos.Where(l => l.FieldName == "default_removed").Should().HaveCount(3)
            .And.OnlyContain(l => l.Operation == "remove_default");
        eventos.Should().NotContain(l => l.FieldName == "deactivated");
    }

    [Fact]
    public async Task HU13138_AC3_la_reasignacion_guarda_tramite_mandatario_anterior_y_nuevo_o_pendiente()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);
        var nuevo = Guid.NewGuid();
        var tramiteA = Guid.NewGuid();
        var tramiteB = Guid.NewGuid();
        var reassigner = new FakeReassigner(new MandateSignerReassignmentResult(
        [
            new MandateSignerProcedureMove(tramiteA, signer, nuevo),
            new MandateSignerProcedureMove(tramiteB, signer, null),
        ]));
        var sut = Build(ctx, reassigner);

        var result = await sut.Inactivate.HandleDetailedAsync(Off(signer), Ct);

        result.Lifecycle!.Reassignment.Reassigned.Should().Be(1);
        result.Lifecycle.Reassignment.Pending.Should().Be(1);
        var eventos = Lifecycle(ctx).Where(l => l.FieldName == "procedure_reassigned").ToList();
        eventos.Should().HaveCount(2).And.OnlyContain(l => l.Operation == "reassign_procedure");

        var payloads = eventos.Select(e => JsonDocument.Parse(e.NewValue!).RootElement).ToList();
        var conNuevo = payloads.Single(p => p.GetProperty("procedureInstanceId").GetGuid() == tramiteA);
        conNuevo.GetProperty("previousSignerId").GetGuid().Should().Be(signer);
        conNuevo.GetProperty("newSignerId").GetGuid().Should().Be(nuevo);
        conNuevo.GetProperty("pendingOtDecision").GetBoolean().Should().BeFalse();
        var pendiente = payloads.Single(p => p.GetProperty("procedureInstanceId").GetGuid() == tramiteB);
        pendiente.GetProperty("newSignerId").ValueKind.Should().Be(JsonValueKind.Null);
        pendiente.GetProperty("pendingOtDecision").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task HU13138_AC4_la_reactivacion_registra_los_vinculos_restaurados()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo"), (Office, CompanyB, "organismo")]);
        await sut.Inactivate.HandleAsync(Off(signer), Ct);

        await sut.Reactivate.HandleAsync(On(signer, MandateSignerActorKind.SuperAdmin), Ct);

        var evento = Lifecycle(ctx).Single(l => l.FieldName == "reactivated");
        evento.Operation.Should().Be("reactivate");
        using var doc = JsonDocument.Parse(evento.NewValue!);
        doc.RootElement.GetProperty("restoredLinks").GetArrayLength().Should().Be(2);
        doc.RootElement.GetProperty("actorRole").GetString().Should().Be("super_admin");
    }

    [Fact]
    public async Task HU13138_AC5_una_operacion_fallida_no_deja_ningun_evento()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);

        (await sut.Delete.HandleAsync(Del(Guid.NewGuid(), confirm: true), Ct)).Outcome.Should().Be(DeleteMandateSignerOutcome.NotFound);
        (await sut.Inactivate.HandleAsync(Off(Guid.NewGuid()), Ct)).Should().Be(InactivateMandateSignerOutcome.NotFound);
        (await sut.Reactivate.HandleAsync(On(signer), Ct)).Should().Be(ReactivateMandateSignerOutcome.NotFound, "ya está activo");

        ctx.TenantConfigAuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task HU13138_AC5_la_baja_rechazada_por_impacto_sin_confirmar_no_deja_evento()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var sut = Build(ctx);
        var signer = AddSigner(ctx, "Ana", [(Office, CompanyA, "organismo")]);

        (await sut.Delete.HandleAsync(Del(signer, confirm: false), Ct)).Outcome
            .Should().Be(DeleteMandateSignerOutcome.ConfirmationRequired);

        ctx.TenantConfigAuditLogs.Should().BeEmpty();
    }
}

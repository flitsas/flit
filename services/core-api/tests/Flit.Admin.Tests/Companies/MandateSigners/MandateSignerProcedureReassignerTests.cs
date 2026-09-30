using Flit.Admin.Application.Companies.MandateSigners.DeleteMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13137 (ADR-0066 P8) — al dar de baja a un mandatario, los trámites radicados sin aprobar que apuntaban a él
/// se reasignan con la prelación del OT usando el evaluador único (mismo directorio real, sobre InMemory). Si nadie
/// resuelve queda en nulo para que el OT decida al aprobar. Borradores y aprobados no se tocan.
/// </summary>
public sealed class MandateSignerProcedureReassignerTests
{
    private static readonly Guid Office = MandateSignerHandlerTests.Office;
    private static readonly Guid Gestora = MandateSignerHandlerTests.CompanyA;
    private static readonly Guid Operator = MandateSignerHandlerTests.Operator;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        FlitDbContext Ctx,
        DeleteMandateSignerHandler Delete,
        InactivateMandateSignerHandler Inactivate,
        IMandateRequirementPolicy Policy);

    private static Harness Build(Guid? otDefault = null)
    {
        var ctx = MandateSignerHandlerTests.NewSeededContext();

        var otStatus = new DbTransitOfficeOperationalStatusReader(ctx);
        var identity = Substitute.For<IProcedureInstanceRepository>();
        identity.ListBiometricValidationsByPersonAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((Array.Empty<ProcedureInstanceBiometricValidation>(), 0, false));
        var directory = new MandateSignerDirectory(
            ctx, Substitute.For<ITransitOfficeOperationalStatusReader>(), new IdentityVigenciaPorDocumentoResolver(identity));

        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveByOfficeIdAsync(Office, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_ => new MandateOtConfig(
                Office, "generico", false, null, null, AssignmentMode: "signer", OtDefaultMandateSignerId: otDefault));

        var personalized = Substitute.For<IPersonalizedDocumentResolver>();
        personalized.ResolveAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PersonalizedDocumentResolution.Empty);

        var evaluator = new MandateSignerEvaluator(directory, policy, null, personalized);
        var reassigner = new MandateSignerProcedureReassigner(ctx, new ProcedureInstanceRepository(ctx), evaluator);

        var reader = new DbMandateSignerReader(ctx);
        var repo = new MandateSignerRepository(ctx, reassigner);
        return new Harness(
            ctx,
            new DeleteMandateSignerHandler(otStatus, reader, new DbMandateSignerImpactReader(ctx), repo),
            new InactivateMandateSignerHandler(otStatus, reader, repo),
            policy);
    }

    private static Guid AddSigner(FlitDbContext ctx, string scope, bool linked = true)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = id, TransitOfficeId = Office, FullName = "Mandatario", DocumentType = "NIT", DocumentNumber = "900111222",
            IntegrityHash = new string('b', 64), RegisteredAt = now, IsActive = true, SignerModel = "juridica", CreatedAt = now,
        });
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = Office, IsActive = true, CreatedAt = now,
        });
        if (linked)
        {
            ctx.MandateSignerCompanies.Add(new MandateSignerCompany
            {
                Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = Office, CompanyTenantId = Gestora,
                IsActive = true, ConfiguredByScope = scope, CreatedAt = now,
            });
        }

        ctx.SaveChanges();
        return id;
    }

    private static Guid AddProcedure(FlitDbContext ctx, Guid signerId, string status)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = Gestora,
            Status = status,
            TransitOfficeId = Office,
            MandateSignerId = signerId,
        };
        ctx.ProcedureInstances.Add(instance);
        ctx.SaveChanges();
        return instance.Id;
    }

    private static Guid? SignerOf(FlitDbContext ctx, Guid procedureId) =>
        ctx.ProcedureInstances.AsNoTracking().Single(p => p.Id == procedureId).MandateSignerId;

    private static DeleteMandateSignerCommand Del(Guid id) =>
        new() { TransitOfficeId = Office, MandateSignerId = id, ConfirmImpact = true, ChangedBy = Operator };

    [Fact]
    public async Task AC1_reasigna_con_el_mandatario_de_la_compania_si_no_hay_uno_del_OT()
    {
        var h = Build();
        await using var ctx = h.Ctx;
        var x = AddSigner(ctx, "organismo");
        var deLaCompania = AddSigner(ctx, "compania");
        var tramite = AddProcedure(ctx, x, TramiteEstado.Entregado);

        var result = await h.Delete.HandleAsync(Del(x), Ct);

        result.Outcome.Should().Be(DeleteMandateSignerOutcome.Deleted);
        SignerOf(ctx, tramite).Should().Be(deLaCompania);
        result.Lifecycle!.Reassignment.Reassigned.Should().Be(1);
        result.Lifecycle.Reassignment.Pending.Should().Be(0);
    }

    [Fact]
    public async Task AC2_el_OT_prevalece_sobre_el_de_la_compania()
    {
        var h = Build();
        await using var ctx = h.Ctx;
        var x = AddSigner(ctx, "compania");
        var deLaCompania = AddSigner(ctx, "compania");
        var delOt = AddSigner(ctx, "organismo");
        var tramite = AddProcedure(ctx, x, TramiteEstado.Entregado);

        await h.Delete.HandleAsync(Del(x), Ct);

        SignerOf(ctx, tramite).Should().Be(delOt);
        SignerOf(ctx, tramite).Should().NotBe(deLaCompania);
    }

    [Fact]
    public async Task AC3_cae_al_default_del_OT_si_la_compania_no_tiene_otro_mandatario()
    {
        var defaultDelOt = Guid.NewGuid();
        var h = Build(defaultDelOt);
        await using var ctx = h.Ctx;
        var now = DateTimeOffset.UtcNow;
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = defaultDelOt, TransitOfficeId = Office, FullName = "Default OT", DocumentType = "NIT",
            DocumentNumber = "900333444", IntegrityHash = new string('c', 64), RegisteredAt = now, IsActive = true,
            SignerModel = "juridica", CreatedAt = now,
        });
        ctx.SaveChanges();
        var x = AddSigner(ctx, "compania");
        var tramite = AddProcedure(ctx, x, TramiteEstado.Asignado);

        await h.Inactivate.HandleAsync(
            new InactivateMandateSignerCommand { TransitOfficeId = Office, MandateSignerId = x, ChangedBy = Operator }, Ct);

        SignerOf(ctx, tramite).Should().Be(defaultDelOt);
    }

    [Fact]
    public async Task AC4_si_nadie_resuelve_queda_en_nulo_y_el_conteo_lo_informa_como_pendiente()
    {
        var h = Build();
        await using var ctx = h.Ctx;
        var x = AddSigner(ctx, "organismo");
        var t1 = AddProcedure(ctx, x, TramiteEstado.Entregado);
        var t2 = AddProcedure(ctx, x, TramiteEstado.Preasignacion);

        var result = await h.Delete.HandleAsync(Del(x), Ct);

        SignerOf(ctx, t1).Should().BeNull();
        SignerOf(ctx, t2).Should().BeNull();
        result.Lifecycle!.Reassignment.Reassigned.Should().Be(0);
        result.Lifecycle.Reassignment.Pending.Should().Be(2);
        result.Lifecycle.Reassignment.Moves.Should().OnlyContain(m => m.IsPending && m.PreviousSignerId == x);
    }

    [Fact]
    public async Task AC6_los_tramites_en_borrador_aprobados_o_firmados_no_cambian()
    {
        var h = Build();
        await using var ctx = h.Ctx;
        var x = AddSigner(ctx, "organismo");
        AddSigner(ctx, "compania");
        var borrador = AddProcedure(ctx, x, TramiteEstado.Borrador);
        var preparado = AddProcedure(ctx, x, TramiteEstado.Preparado);
        var aprobado = AddProcedure(ctx, x, TramiteEstado.Aprobado);
        var revocado = AddProcedure(ctx, x, TramiteEstado.Revocado);
        var pendiente = AddProcedure(ctx, x, TramiteEstado.Entregado);

        var result = await h.Delete.HandleAsync(Del(x), Ct);

        SignerOf(ctx, borrador).Should().Be(x, "el FUR del borrador se recalcula solo");
        SignerOf(ctx, preparado).Should().Be(x);
        SignerOf(ctx, aprobado).Should().Be(x, "los aprobados conservan quién firmó");
        SignerOf(ctx, revocado).Should().Be(x);
        SignerOf(ctx, pendiente).Should().NotBe(x);
        result.Lifecycle!.Reassignment.Moves.Should().ContainSingle(m => m.ProcedureInstanceId == pendiente);
    }

    [Fact]
    public async Task Sin_tramites_afectados_la_baja_no_reasigna_nada()
    {
        var h = Build();
        await using var ctx = h.Ctx;
        var x = AddSigner(ctx, "organismo");

        var result = await h.Delete.HandleAsync(Del(x), Ct);

        result.Lifecycle!.Reassignment.Moves.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_tramite_que_apunta_a_otro_mandatario_no_se_toca()
    {
        var h = Build();
        await using var ctx = h.Ctx;
        var x = AddSigner(ctx, "organismo");
        var otro = AddSigner(ctx, "compania");
        var deOtro = AddProcedure(ctx, otro, TramiteEstado.Entregado);

        await h.Delete.HandleAsync(Del(x), Ct);

        SignerOf(ctx, deOtro).Should().Be(otro);
    }
}

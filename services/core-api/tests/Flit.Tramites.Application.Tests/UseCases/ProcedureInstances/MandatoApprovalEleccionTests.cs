using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13156 — con el PUT y la lista de mandatarios del gestor retirados, la aprobación del OT sigue
/// resolviendo el mandatario con la prelación (<c>MandatoApprovalHandler</c>) y el resolver compartido del
/// organismo y del NIT mandante (<see cref="MandateSignerSelectionResolver"/>) conserva su cobertura.
/// </summary>
public sealed class MandatoApprovalEleccionTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-1111-4000-8000-000000000001");
    private static readonly Guid Ot = Guid.Parse("bbbbbbbb-1111-4000-8000-000000000001");
    private static readonly Guid Ana = Guid.Parse("cccccccc-1111-4000-8000-000000000001");
    private static readonly Guid Carlos = Guid.Parse("cccccccc-1111-4000-8000-000000000002");
    private static readonly DateTimeOffset Hasta = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    /// <summary>Directorio con los mandatarios que se le pasen, solo para el OT y la compañía dados.</summary>
    private sealed class Directorio(params MandateSignerCandidate[] candidatos) : IMandateSignerDirectory
    {
        public Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
            Guid transitOfficeId, Guid companyTenantId, string? nitMandante = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MandateSignerCandidate>>(
                transitOfficeId == Ot && companyTenantId == Tenant ? candidatos : []);

        public Task<MandateSignerCandidate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(candidatos.FirstOrDefault(c => c.Id == id));
    }

    private static MandateSignerCandidate Candidato(
        Guid id, string nombre, bool vigente = true, DateTimeOffset? hasta = null) =>
        new(id, nombre, "1020304050", null, vigente, null, "CC", null, hasta);

    /// <summary>Trámite con el organismo donde lo deja el wizard: en field_values, no en la columna.</summary>
    private ProcedureInstance Instancia(string estado = TramiteEstado.Borrador, Guid? elegido = null)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteModalidadEntradaCodes.MatriculaInicial),
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "MAT-2026-000001",
            Status = estado,
            MandateSignerId = elegido,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "transit_office_id",
            ValueText = Ot.ToString(),
            Source = "user",
        });

        _repo.GetByIdWithDetailsAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    // ── AC1/AC2 — qué se ofrece y con qué datos ───────────────────────────────



    // ── La resolución automática queda como respaldo ──────────────────────────

    /// <summary>
    /// La trampa que el plan señaló: adelantar la elección al registro NO puede romper los trámites que
    /// no traen ninguna. Con elección, esa manda al aprobar; sin ella, sigue la resolución automática.
    /// </summary>
    [Fact]
    public async Task AlAprobar_LaEleccionDelRegistroManda()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(estado: TramiteEstado.Preparado, elegido: Carlos);
        instance.TransitOfficeId = Ot;
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = instance.Id,
            Tipo = "mandato",
        });
        _repo.GetByIdWithFurGraphAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);

        var handler = new MandatoApprovalHandler(
            _repo, new Directorio(Candidato(Ana, "Ana Restrepo"), Candidato(Carlos, "Carlos Pérez")));

        // Sin la elección del registro, dos candidatos sin cotejo obligarían al OT a elegir (409).
        var decision = await handler.CheckAsync(instance.Id, Tenant, approvingUserId: null, explicitSignerId: null, ct);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(Carlos);
    }

    [Fact]
    public async Task AlAprobar_SinEleccionEnElRegistro_SigueLaResolucionAutomatica()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(estado: TramiteEstado.Preparado);
        instance.TransitOfficeId = Ot;
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = instance.Id,
            Tipo = "mandato",
        });
        _repo.GetByIdWithFurGraphAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);

        var handler = new MandatoApprovalHandler(_repo, new Directorio(Candidato(Ana, "Ana Restrepo")));

        var decision = await handler.CheckAsync(instance.Id, Tenant, null, null, ct);

        // Un único candidato: se resuelve solo, como siempre.
        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(Ana);
    }

    /// <summary>
    /// Bug DEV — antes de unificar la resolución, el gate de aprobación NO conocía el default del OT (solo
    /// lo usaba el listado de pantalla): con varios candidatos y sin elección, dependía ÚNICAMENTE del
    /// cotejo por cuenta de usuario y, sin match, exigía seleccionar (409) aunque el OT ya tuviera un
    /// mandatario preferido configurado. Ahora el default parametrizado se aplica ANTES del cotejo, igual
    /// que en pantalla y en el documento.
    /// </summary>
    [Fact]
    public async Task AlAprobar_SinEleccionNiCotejoDeUsuario_ElDefaultDelOtResuelveSolo()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(estado: TramiteEstado.Preparado);
        instance.TransitOfficeId = Ot;
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "transit_office_code",
            ValueText = "05001",
            Source = "user",
        });
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = instance.Id,
            Tipo = "mandato",
        });
        _repo.GetByIdWithFurGraphAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);

        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveAsync("05001", Tenant, Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(
                Ot, "generico", RequiresForNaturalPerson: false, null, null,
                AssignmentMode: "signer", DefaultMandateSignerId: Carlos));

        var handler = new MandatoApprovalHandler(
            _repo,
            new Directorio(Candidato(Ana, "Ana Restrepo"), Candidato(Carlos, "Carlos Pérez")),
            mandatePolicy: policy);

        // approvingUserId sin match con ningún candidato: antes hubiera exigido seleccionar (409).
        var decision = await handler.CheckAsync(instance.Id, Tenant, approvingUserId: Guid.NewGuid(), explicitSignerId: null, ct);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(Carlos);
    }


    // ── HU #13156 (AC2) — el resolver compartido (internal) sobrevive al retiro de los handlers ───

    [Fact]
    public async Task AlAprobar_ElNitMandanteEsElDelVendedor_NuncaElDelComprador()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(estado: TramiteEstado.Preparado);
        instance.TransitOfficeId = Ot;
        instance.Actors.Add(new ProcedureInstanceActor { ActorType = "comprador", DocumentNumber = "111" });
        instance.Actors.Add(new ProcedureInstanceActor { ActorType = "vendedor", DocumentNumber = " 900123 " });
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = instance.Id,
            Tipo = "mandato",
        });
        _repo.GetByIdWithFurGraphAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);

        var directorio = Substitute.For<IMandateSignerDirectory>();
        directorio.GetCandidatesAsync(Ot, Tenant, "900123", Arg.Any<CancellationToken>())
            .Returns(new List<MandateSignerCandidate> { Candidato(Ana, "Ana Restrepo") });

        var decision = await new MandatoApprovalHandler(_repo, directorio).CheckAsync(instance.Id, Tenant, null, null, ct);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(Ana);
        await directorio.Received().GetCandidatesAsync(Ot, Tenant, "900123", Arg.Any<CancellationToken>());
    }
}

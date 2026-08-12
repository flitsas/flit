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
/// HU #11203 — el gestor elige QUIÉN firma el mandato al registrar el trámite. Hasta ahora eso se
/// resolvía al aprobar: el gestor no sabía quién iba a firmar y, con varios mandatarios, era el
/// organismo el que tenía que decidirlo con el trámite ya radicado.
/// </summary>
public sealed class MandateSignerSelectionTests
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
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "MAT-2026-000001",
            Status = estado,
            ModalidadEntrada = TramiteModalidadEntradaCodes.MatriculaInicial,
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

    [Fact]
    public async Task AC1_SeMuestranLosMandatariosHabilitadosParaElOrganismoDeEsaCompania()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia();
        var handler = new ListMandateSignerOptionsHandler(
            _repo, new Directorio(Candidato(Ana, "Ana Restrepo"), Candidato(Carlos, "Carlos Pérez")));

        var (result, error) = await handler.HandleAsync(instance.Id, Tenant, ct);

        error.Should().BeNull();
        result!.Opciones.Select(o => o.Nombre).Should().BeEquivalentTo(["Ana Restrepo", "Carlos Pérez"]);
    }

    [Fact]
    public async Task AC2_CadaMandatarioTraeNombreDocumentoYVigenciaDeSuIdentidad()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia();
        var handler = new ListMandateSignerOptionsHandler(
            _repo,
            new Directorio(
                Candidato(Ana, "Ana Restrepo", vigente: true, hasta: Hasta),
                Candidato(Carlos, "Carlos Pérez", vigente: false)));

        var (result, _) = await handler.HandleAsync(instance.Id, Tenant, ct);

        var ana = result!.Opciones.Single(o => o.Id == Ana);
        ana.TipoDocumento.Should().Be("CC");
        ana.Documento.Should().Be("1020304050");
        ana.IdentidadVigente.Should().BeTrue();
        ana.IdentidadHasta.Should().Be(Hasta);

        // Sin identidad vigente el mandato no se puede aprobar: se avisa al elegir, no al final.
        result.Opciones.Single(o => o.Id == Carlos).IdentidadVigente.Should().BeFalse();
    }

    // ── AC3 — uno solo queda por defecto ──────────────────────────────────────

    [Fact]
    public async Task AC3_ConUnUnicoMandatarioHabilitado_QuedaElegidoPorDefecto()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia();
        var handler = new ListMandateSignerOptionsHandler(_repo, new Directorio(Candidato(Ana, "Ana Restrepo")));

        var (result, _) = await handler.HandleAsync(instance.Id, Tenant, ct);

        result!.ElegidoId.Should().Be(Ana);
    }

    [Fact]
    public async Task ConVarios_NoSePreseleccionaNinguno()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia();
        var handler = new ListMandateSignerOptionsHandler(
            _repo, new Directorio(Candidato(Ana, "Ana Restrepo"), Candidato(Carlos, "Carlos Pérez")));

        var (result, _) = await handler.HandleAsync(instance.Id, Tenant, ct);

        // Elegir por el gestor cuando hay más de uno sería decidir por él quién firma.
        result!.ElegidoId.Should().BeNull();
    }

    [Fact]
    public async Task TipoInstitucional_NoOfreceCandidatosPersona()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia();
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "transit_office_code",
            ValueText = "5631000",
            Source = "user",
        });

        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveAsync("5631000", Tenant, Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(
                Ot,
                "sabaneta",
                RequiresForNaturalPerson: true,
                "UT-SETSA",
                "900273813-7",
                AssignmentMode: "institutional"));

        var handler = new ListMandateSignerOptionsHandler(
            _repo,
            new Directorio(Candidato(Ana, "Ana Restrepo"), Candidato(Carlos, "Carlos Pérez")),
            mandatePolicy: policy);

        var (result, error) = await handler.HandleAsync(instance.Id, Tenant, ct);

        error.Should().BeNull();
        result!.Opciones.Should().BeEmpty();
        result.ElegidoId.Should().BeNull();
    }

    [Fact]
    public async Task DefaultParametrizado_SePreseleccionaSiEstaEntreOpciones()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia();
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "transit_office_code",
            ValueText = "05001",
            Source = "user",
        });

        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveAsync("05001", Tenant, Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(
                Ot,
                "generico",
                RequiresForNaturalPerson: false,
                InstitutionalMandataryName: null,
                InstitutionalMandataryNit: null,
                AssignmentMode: "signer",
                DefaultMandateSignerId: Carlos));

        var handler = new ListMandateSignerOptionsHandler(
            _repo,
            new Directorio(Candidato(Ana, "Ana Restrepo"), Candidato(Carlos, "Carlos Pérez")),
            mandatePolicy: policy);

        var (result, error) = await handler.HandleAsync(instance.Id, Tenant, ct);

        error.Should().BeNull();
        result!.Opciones.Should().HaveCount(2);
        result.ElegidoId.Should().Be(Carlos);
    }

    // ── AC4 — cambio en borrador ──────────────────────────────────────────────

    [Fact]
    public async Task AC4_EnBorrador_ElCambioDeMandatarioQuedaGuardado()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(elegido: Ana);
        var handler = new SetMandateSignerHandler(
            _repo, new Directorio(Candidato(Ana, "Ana Restrepo"), Candidato(Carlos, "Carlos Pérez")));

        var error = await handler.HandleAsync(instance.Id, Tenant, Carlos, ct);

        error.Should().BeNull();
        instance.MandateSignerId.Should().Be(Carlos);
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task UnMandatarioAjenoAlOrganismo_NoSePuedeElegir()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia();
        var handler = new SetMandateSignerHandler(_repo, new Directorio(Candidato(Ana, "Ana Restrepo")));

        // Aceptar cualquier id dejaría el trámite apuntando a alguien que no puede firmarlo, y eso solo
        // se descubriría al aprobar.
        var error = await handler.HandleAsync(instance.Id, Tenant, Carlos, ct);

        error.Should().Be("mandatario_no_habilitado");
        instance.MandateSignerId.Should().BeNull();
        await _repo.DidNotReceiveWithAnyArgs().SaveChangesAsync(ct);
    }

    // ── AC5 — fuera de borrador se muestra pero no se cambia ──────────────────

    [Fact]
    public async Task AC5_FueraDeBorrador_ElMandatarioSeMuestraPeroNoSePuedeCambiar()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(estado: TramiteEstado.Entregado, elegido: Ana);
        var directorio = new Directorio(Candidato(Ana, "Ana Restrepo"), Candidato(Carlos, "Carlos Pérez"));

        var (result, _) = await new ListMandateSignerOptionsHandler(_repo, directorio)
            .HandleAsync(instance.Id, Tenant, ct);

        result!.ElegidoId.Should().Be(Ana);
        result.Editable.Should().BeFalse();

        var error = await new SetMandateSignerHandler(_repo, directorio)
            .HandleAsync(instance.Id, Tenant, Carlos, ct);

        // Cambiarlo alteraría un documento ya generado y radicado.
        error.Should().Be("not_draft");
        instance.MandateSignerId.Should().Be(Ana);
    }

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
}

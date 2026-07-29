using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class ActorsHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    // HU #10880: proveedor mock por defecto (igual que el resto de la suite) — el reenvío automático de
    // identidad solo actúa cuando el proveedor es kyverum; los tests dedicados lo activan explícitamente.
    private readonly BiometricsProviderOptions _providerOptions = new() { Provider = BiometricProviders.Mock };
    private readonly IKyverumVerifyClient _kyverumClient = Substitute.For<IKyverumVerifyClient>();
    private readonly IniciarKyverumVerifyHandler _kyverumHandler;
    // HU #10878: gate de consentimiento Habeas Data (ADR-0031). Sin stub, NSubstitute lo trata como
    // "sin fila previa" — el upsert crea una nueva cuando el test manda AutorizaReutilizacionDatos=true.
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly PutActorsHandler _put;
    private readonly GetActorsHandler _get;

    private static readonly Guid BuyerEntityId = Guid.NewGuid();
    private static readonly Guid OwnerEntityId = Guid.NewGuid();

    public ActorsHandlerTests()
    {
        _kyverumHandler = new IniciarKyverumVerifyHandler(
            _repo,
            _kyverumClient,
            new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(),
            Substitute.For<IIdentityValidationAuditLog>());
        _put = new PutActorsHandler(_repo, _catalogRepo, _providerOptions, _kyverumHandler, _consentRepo);
        _get = new GetActorsHandler(_repo);

        _catalogRepo.GetProcedureEntityByCodeAsync("BUYER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = BuyerEntityId, Code = "BUYER", Name = "Comprador" });
        _catalogRepo.GetProcedureEntityByCodeAsync("OWNER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = OwnerEntityId, Code = "OWNER", Name = "Propietario" });
    }

    private static ProcedureInstance Instance(
        Guid id,
        Guid tenantId,
        string modalidad = "matricula_inicial",
        string status = TramiteEstado.Borrador,
        string? tipologia = null) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = modalidad,
            TipologiaCodigo = tipologia,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ActorInput Comprador(string doc = "123", string email = "comprador@x.com") =>
        new("comprador", "CC", doc, "Juan Comprador", email, "3001112233");

    private static ActorInput Vendedor(string doc = "999", string email = "vendedor@x.com") =>
        new("vendedor", "CC", doc, "Pedro Vendedor", email, null);

    // ── 404 / 409 estado ──────────────────────────────────────────────────────

    [Fact]
    public async Task Put_InstanceNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithBiometricsAndActorsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await _put.HandleAsync(Guid.NewGuid(), Guid.NewGuid(),
            new PutActorsRequest([Comprador()]), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("submitted")]
    [InlineData("completed")]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Rechazado)]
    public async Task Put_NotDraft_ReturnsConflict(string status)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, status: status));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([Comprador()]), ct);

        error.Should().Be("not_draft");
        result.Should().BeNull();
    }

    // HU #10870 (AC1) — subsanación reabre la edición COMPLETA del trámite (entregado/rechazado →
    // subsanacion) SIN pasar por borrador: los actores deben poder editarse en este estado, igual que
    // PatchFieldValuesHandler y el trigger de BD (trg_field_value_immutable).
    [Fact]
    public async Task Put_Subsanacion_PermiteEditarActores()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, status: TramiteEstado.Rechazado);
        instance.SubsanacionActiva = true;
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct)
            .Returns(instance);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([Comprador()]), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Actors.Should().ContainSingle(a => a.NumeroDocumento == "123");
    }

    // ── Charset del número de documento (Ajuste 3) ────────────────────────────

    [Theory]
    [InlineData("CC", "12A4")]   // cédula con letra
    [InlineData("CE", "12.34")]  // con puntuación
    [InlineData("NIT", "900-1")] // NIT con guion (solo dígitos)
    [InlineData("TI", "10 20")]  // con espacio
    public async Task Put_DocumentoNoNumerico_ReturnsInvalidDocumentNumber(string tipo, string doc)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "matricula_inicial"));

        var actor = new ActorInput("comprador", tipo, doc, "Juan Comprador", "c@x.com", null);
        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);

        error.Should().Be("invalid_document_number");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_PasaporteAlfanumerico_EsValido()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "matricula_inicial"));

        var actor = new ActorInput("comprador", "PAS", "AB123CD", "Juan Comprador", "c@x.com", null);
        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
    }

    // ── HU #10542 / #10544: tipo de persona y representante legal ──────────────

    [Fact]
    public async Task Put_PersonaNatural_PersisteTipoYRepresentanteLegal()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "traspaso");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var vendedor = new ActorInput(
            "vendedor", "CC", "999", "Pedro Vendedor", "vendedor@x.com", null,
            PersonType: "NATURAL", EsRepresentanteLegal: true);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([vendedor]), ct);

        error.Should().BeNull();
        var dto = result!.Actors.Should().ContainSingle().Subject;
        dto.PersonType.Should().Be("natural"); // normalizado a minúsculas
        dto.EsRepresentanteLegal.Should().BeTrue();
        instance.Actors.Should().ContainSingle()
            .Which.PersonType.Should().Be("natural");
    }

    [Fact]
    public async Task Put_SinTipoPersona_QuedaNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([Comprador()]), ct);

        error.Should().BeNull();
        result!.Actors[0].PersonType.Should().BeNull();
        result.Actors[0].EsRepresentanteLegal.Should().BeFalse();
    }

    [Fact]
    public async Task Put_TipoPersonaInvalido_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "matricula_inicial"));

        var actor = new ActorInput("comprador", "CC", "123", "Juan", "c@x.com", null, PersonType: "persona_natural");
        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);

        error.Should().Be("invalid_person_type");
        result.Should().BeNull();
    }

    // ── HU #10688 (AC1): correo del representante legal obligatorio en persona jurídica ──

    [Fact]
    public async Task Put_PersonaJuridica_SinCorreoRL_ReturnsRlEmailRequerido()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "matricula_inicial"));

        // RL sin correo (solo nombre) → debe rechazar en PJ.
        var rl = new ActorRepresentanteLegal("CC", "123", "Rep Legal", null, null);
        var actor = new ActorInput("comprador", "NIT", "900123456", "ACME S.A.S.", "empresa@x.com", null,
            PersonType: "juridical", RepresentanteLegal: rl);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);

        error.Should().Be("rl_email_requerido");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_PersonaJuridica_SinRepresentanteLegal_ReturnsRlEmailRequerido()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "matricula_inicial"));

        var actor = new ActorInput("comprador", "NIT", "900123456", "ACME S.A.S.", "empresa@x.com", null,
            PersonType: "juridical");

        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);

        error.Should().Be("rl_email_requerido");
    }

    [Fact]
    public async Task Put_PersonaJuridica_CorreoRLInvalido_ReturnsRlEmailRequerido()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "matricula_inicial"));

        var rl = new ActorRepresentanteLegal("CC", "123", "Rep Legal", "not-an-email", null);
        var actor = new ActorInput("comprador", "NIT", "900123456", "ACME S.A.S.", "empresa@x.com", null,
            PersonType: "juridical", RepresentanteLegal: rl);

        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);

        error.Should().Be("rl_email_requerido");
    }

    [Fact]
    public async Task Put_PersonaJuridica_ConCorreoRLValido_Persiste()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var rl = new ActorRepresentanteLegal("CC", "123", "Rep Legal", "rl@x.com", "3001112233");
        var actor = new ActorInput("comprador", "NIT", "900123456", "ACME S.A.S.", "empresa@x.com", null,
            PersonType: "juridical", RepresentanteLegal: rl);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);

        error.Should().BeNull();
        var dto = result!.Actors.Should().ContainSingle().Subject;
        dto.PersonType.Should().Be("juridical");
        dto.RepresentanteLegal!.Email.Should().Be("rl@x.com");
    }

    [Fact]
    public async Task Put_PersonaNatural_SinRL_NoAplicaReglaRlEmail()
    {
        // AC5: la ruta de persona natural no exige datos del RL.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var actor = new ActorInput("comprador", "CC", "123", "Juan Comprador", "c@x.com", null, PersonType: "natural");
        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);

        error.Should().BeNull();
    }

    // ── Roles por modalidad ───────────────────────────────────────────────────

    [Fact]
    public async Task Put_MatriculaInicial_HappyPath_SavesComprador()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([Comprador()]), ct);

        error.Should().BeNull();
        result!.Actors.Should().ContainSingle();
        result.Actors[0].Rol.Should().Be("comprador");
        instance.Actors.Should().ContainSingle()
            .Which.ProcedureEntityId.Should().Be(BuyerEntityId);
        // Los actores re-agregados tras el Clear() se marcan Added explícito → INSERT
        // (PK store-generated con Id ya seteado).
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceActor>(a => a.ActorType == "comprador"));
        // Reemplazo total = 2 SaveChanges: 1 tras Clear() (DELETE) + 1 tras re-add (INSERT),
        // para no violar UNIQUE(procedure_instance_id, procedure_entity_id) en un re-PUT.
        await _repo.Received(2).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Put_MatriculaInicial_RejectsVendedor()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "matricula_inicial"));

        var (result, error) = await _put.HandleAsync(id, tenant,
            new PutActorsRequest([Comprador(), Vendedor()]), ct);

        error.Should().Be("rol_not_allowed");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_Traspaso_HappyPath_SavesAmbasPartes()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "traspaso");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _put.HandleAsync(id, tenant,
            new PutActorsRequest([Vendedor(), Comprador()]), ct);

        error.Should().BeNull();
        result!.Actors.Should().HaveCount(2);
        instance.Actors.Should().Contain(a => a.ActorType == "vendedor" && a.ProcedureEntityId == OwnerEntityId);
        instance.Actors.Should().Contain(a => a.ActorType == "comprador" && a.ProcedureEntityId == BuyerEntityId);
    }

    // PUT incremental (upsert por rol): el wizard guarda un rol por paso, así que un PUT
    // con un solo rol NO debe fallar por "faltan partes obligatorias".

    [Fact]
    public async Task Put_Traspaso_OnlyVendedor_ShouldSucceed()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "traspaso");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([Vendedor()]), ct);

        error.Should().BeNull();
        result!.Actors.Should().ContainSingle().Which.Rol.Should().Be("vendedor");
        instance.Actors.Should().ContainSingle()
            .Which.ProcedureEntityId.Should().Be(OwnerEntityId);
    }

    [Fact]
    public async Task Put_Traspaso_OnlyComprador_ShouldSucceed()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "traspaso");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([Comprador()]), ct);

        error.Should().BeNull();
        result!.Actors.Should().ContainSingle().Which.Rol.Should().Be("comprador");
        instance.Actors.Should().ContainSingle()
            .Which.ProcedureEntityId.Should().Be(BuyerEntityId);
    }

    [Fact]
    public async Task Put_Traspaso_VendedorThenComprador_MergesToTwoActors()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "traspaso");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        // Paso 3: guarda solo vendedor.
        var (_, err1) = await _put.HandleAsync(id, tenant, new PutActorsRequest([Vendedor()]), ct);
        err1.Should().BeNull();

        // Paso 4: guarda solo comprador → NO borra al vendedor; el set efectivo tiene ambos.
        var (result, err2) = await _put.HandleAsync(id, tenant, new PutActorsRequest([Comprador()]), ct);

        err2.Should().BeNull();
        result!.Actors.Should().HaveCount(2);
        instance.Actors.Should().Contain(a => a.ActorType == "vendedor" && a.ProcedureEntityId == OwnerEntityId);
        instance.Actors.Should().Contain(a => a.ActorType == "comprador" && a.ProcedureEntityId == BuyerEntityId);
    }

    [Fact]
    public async Task Put_ResolvesRolesByTipologia_WhenSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        // modalidad ambigua pero tipología fija traspaso_standard
        var instance = Instance(id, tenant, modalidad: "traspaso", tipologia: "traspaso_standard");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _put.HandleAsync(id, tenant,
            new PutActorsRequest([Vendedor(), Comprador()]), ct);

        error.Should().BeNull();
    }

    // ── Unicidad vendedor ≠ comprador (dominio TraspasoPartes) ─────────────────

    [Fact]
    public async Task Put_Traspaso_SameDocument_ReturnsPartesDuplicadas()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "traspaso"));

        var (result, error) = await _put.HandleAsync(id, tenant,
            new PutActorsRequest([Vendedor(doc: "555"), Comprador(doc: "555")]), ct);

        error.Should().Be("partes_duplicadas");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_Traspaso_SameEmail_ReturnsPartesDuplicadas()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "traspaso"));

        var (result, error) = await _put.HandleAsync(id, tenant,
            new PutActorsRequest([Vendedor(email: "same@x.com"), Comprador(email: "SAME@x.com")]), ct);

        error.Should().Be("partes_duplicadas");
        result.Should().BeNull();
    }

    // ── Validación de forma ───────────────────────────────────────────────────

    [Fact]
    public async Task Put_InvalidDocumentType_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var bad = new ActorInput("comprador", "XX", "123", "Juan", "j@x.com", null);
        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([bad]), ct);

        error.Should().Be("invalid_document_type");
    }

    [Fact]
    public async Task Put_InvalidEmail_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var bad = new ActorInput("comprador", "CC", "123", "Juan", "not-an-email", null);
        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([bad]), ct);

        error.Should().Be("invalid_email");
    }

    [Fact]
    public async Task Put_UnknownRol_ReturnsInvalidRol()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var bad = new ActorInput("heredero", "CC", "123", "Juan", "j@x.com", null);
        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([bad]), ct);

        error.Should().Be("invalid_rol");
    }

    [Fact]
    public async Task Put_DuplicateRol_ReturnsDuplicateRol()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (_, error) = await _put.HandleAsync(id, tenant,
            new PutActorsRequest([Comprador(doc: "1"), Comprador(doc: "2")]), ct);

        error.Should().Be("duplicate_rol");
    }

    // ── Reemplazo total del set ───────────────────────────────────────────────

    [Fact]
    public async Task Put_ReplacesExistingActorSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "traspaso");
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentNumber = "OLD",
            ProcedureEntityId = BuyerEntityId,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _put.HandleAsync(id, tenant,
            new PutActorsRequest([Vendedor(), Comprador(doc: "456")]), ct);

        error.Should().BeNull();
        result!.Actors.Should().HaveCount(2);
        instance.Actors.Should().NotContain(a => a.DocumentNumber == "OLD");
    }

    // ── GET ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_NotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithActorsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await _get.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Get_ReturnsSavedActors()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "123",
            FullName = "Juan Comprador",
            Email = "comprador@x.com",
            Phone = "3001112233",
            ProcedureEntityId = BuyerEntityId,
        });
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _get.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Actors.Should().ContainSingle();
        var dto = result.Actors[0];
        dto.Rol.Should().Be("comprador");
        dto.TipoDocumento.Should().Be("CC");
        dto.NumeroDocumento.Should().Be("123");
        dto.NombreCompleto.Should().Be("Juan Comprador");
        dto.Email.Should().Be("comprador@x.com");
        dto.Telefono.Should().Be("3001112233");
    }

    // ── Ciudad / dirección (metadata JSON) ─────────────────────────────────────

    [Fact]
    public async Task Put_PersistsCiudadDireccion_InActorMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var comprador = new ActorInput("comprador", "CC", "123", "Juan Comprador", "comprador@x.com", "3001112233", "Medellin", "Calle 10 #20-30");
        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([comprador]), ct);

        error.Should().BeNull();
        // El handler vuelca ciudad/dirección en metadata JSON del actor persistido…
        var saved = instance.Actors.Should().ContainSingle().Subject;
        saved.Metadata.Should().Contain("Medellin").And.Contain("Calle 10 #20-30");
        // …y el DTO de respuesta los re-expone leyendo ese metadata.
        result!.Actors[0].Ciudad.Should().Be("Medellin");
        result.Actors[0].Direccion.Should().Be("Calle 10 #20-30");
    }

    [Fact]
    public async Task Get_ReadsCiudadDireccion_FromActorMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "123",
            FullName = "Juan Comprador",
            Email = "comprador@x.com",
            Phone = "3001112233",
            ProcedureEntityId = BuyerEntityId,
            Metadata = "{\"ciudad\":\"Cali\",\"direccion\":\"Av Siempre Viva 742\"}",
        });
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _get.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        var dto = result!.Actors[0];
        dto.Ciudad.Should().Be("Cali");
        dto.Direccion.Should().Be("Av Siempre Viva 742");
    }

    [Fact]
    public async Task Get_EmptyMetadata_CiudadDireccionNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "123",
            FullName = "Juan Comprador",
            Email = "comprador@x.com",
            ProcedureEntityId = BuyerEntityId,
            Metadata = "{}",
        });
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(instance);

        var (result, _) = await _get.HandleAsync(id, tenant, ct);

        result!.Actors[0].Ciudad.Should().BeNull();
        result.Actors[0].Direccion.Should().BeNull();
    }

    // ── HU #10880: reenvío de identidad al cambiar el correo del actor ─────────

    private static ProcedureInstanceBiometricValidation SentValidation(
        string parte, string tipoDoc, string documento, string email, string status = BiometricEstados.EnProceso) =>
        new()
        {
            Id = Guid.NewGuid(),
            PartyRole = parte,
            Name = "Sujeto",
            DocumentType = tipoDoc,
            DocumentNumber = documento,
            Email = email,
            Status = status,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private void StubKyverumOk(string verificationId = "kyv_999", string captureUrl = "https://capture/kyv_999") =>
        _kyverumClient.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult(verificationId, captureUrl, "whsec_x", "pending", "{}"));

    [Fact]
    public async Task Put_EmailChanged_Kyverum_ExpiraPreviaYReenviaConNuevoCaptureUrl()
    {
        // AC1: correo del actor cambia -> la validación previa (enviada) queda expirada y se genera/reenvía
        // un CaptureUrl nuevo al correo actualizado, reutilizando IniciarKyverumVerifyHandler.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "123",
            FullName = "Juan Comprador",
            Email = "viejo@x.com",
            ProcedureEntityId = BuyerEntityId,
        });
        var previa = SentValidation("comprador", "CC", "123", "viejo@x.com");
        instance.BiometricValidations.Add(previa);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        _providerOptions.Provider = BiometricProviders.Kyverum;
        StubKyverumOk();

        var (result, error) = await _put.HandleAsync(
            id, tenant, new PutActorsRequest([Comprador(doc: "123", email: "nuevo@x.com")]), ct);

        error.Should().BeNull();
        result!.Actors.Should().ContainSingle().Which.Email.Should().Be("nuevo@x.com");

        previa.Status.Should().Be(BiometricEstados.Expirado);
        instance.BiometricValidations.Should().Contain(v =>
            v.Status == BiometricEstados.EnProceso
            && v.CaptureUrl == "https://capture/kyv_999"
            && v.Email == "nuevo@x.com");

        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r => r.Email == "nuevo@x.com" && r.Parte == "comprador"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_EmailChanged_IdentidadYaAprobada_NoLaExpiraNiReenvia()
    {
        // La identidad APROBADA no se toca al corregir un correo: el AC habla de una validación
        // "enviada", y expirar una aprobación obligaría a revalidar a quien ya validó, rompiendo la
        // radicación. La aprobación vigente se conserva para su reúso (HU #10350).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "123",
            FullName = "Juan Comprador",
            Email = "viejo@x.com",
            ProcedureEntityId = BuyerEntityId,
        });
        var aprobada = SentValidation("comprador", "CC", "123", "viejo@x.com", BiometricEstados.Aprobado);
        instance.BiometricValidations.Add(aprobada);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        _providerOptions.Provider = BiometricProviders.Kyverum;
        StubKyverumOk();

        var (result, error) = await _put.HandleAsync(
            id, tenant, new PutActorsRequest([Comprador(doc: "123", email: "nuevo@x.com")]), ct);

        error.Should().BeNull();
        result!.Actors.Should().ContainSingle().Which.Email.Should().Be("nuevo@x.com");

        aprobada.Status.Should().Be(BiometricEstados.Aprobado);
        instance.BiometricValidations.Should().ContainSingle();
        await _kyverumClient.DidNotReceiveWithAnyArgs()
            .StartVerificationAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Put_EmailIgual_NoReenviaValidacion()
    {
        // AC2: el correo se guarda igual (aun con distinto casing/espacios) -> no se toca la biométrica.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "123",
            FullName = "Juan Comprador",
            Email = "mismo@x.com",
            ProcedureEntityId = BuyerEntityId,
        });
        var previa = SentValidation("comprador", "CC", "123", "mismo@x.com");
        instance.BiometricValidations.Add(previa);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        _providerOptions.Provider = BiometricProviders.Kyverum;

        var (_, error) = await _put.HandleAsync(
            id, tenant, new PutActorsRequest([Comprador(doc: "123", email: "  MISMO@X.com  ")]), ct);

        error.Should().BeNull();
        previa.Status.Should().Be(BiometricEstados.EnProceso); // sin cambios.
        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_EmailChanged_SinValidacionPrevia_NoOp()
    {
        // Sin ninguna validación previamente enviada para la parte: nada que expirar/reenviar (precondición
        // de AC1). El PUT del actor debe seguir funcionando con normalidad.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "123",
            FullName = "Juan Comprador",
            Email = "viejo@x.com",
            ProcedureEntityId = BuyerEntityId,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        _providerOptions.Provider = BiometricProviders.Kyverum;

        var (_, error) = await _put.HandleAsync(
            id, tenant, new PutActorsRequest([Comprador(doc: "123", email: "nuevo@x.com")]), ct);

        error.Should().BeNull();
        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_EmailChanged_MockProvider_SoloExpiraNoAutoReenvia()
    {
        // Proveedor mock: no hay CaptureUrl/envío real de Kyverum que reutilizar. La previa NO se toca aquí
        // (el gestor puede reiniciar manualmente vía POST biometric si aplica).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "123",
            FullName = "Juan Comprador",
            Email = "viejo@x.com",
            ProcedureEntityId = BuyerEntityId,
        });
        var previa = SentValidation("comprador", "CC", "123", "viejo@x.com");
        previa.Provider = BiometricProviders.Mock;
        instance.BiometricValidations.Add(previa);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        // _providerOptions ya es Mock por defecto (constructor de la suite).

        var (_, error) = await _put.HandleAsync(
            id, tenant, new PutActorsRequest([Comprador(doc: "123", email: "nuevo@x.com")]), ct);

        error.Should().BeNull();
        previa.Status.Should().Be(BiometricEstados.EnProceso); // no se expira: proveedor mock no aplica AC1.
        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_EmailChanged_PersonaJuridica_UsaCorreoDelRepresentanteLegal()
    {
        // PJ (HU #10688): el sujeto de identidad es el representante legal. Cambiar el correo del RL debe
        // disparar el reenvío aunque el correo de la empresa (actor.Email) no cambie.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ActorType = "comprador",
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "ACME S.A.S.",
            Email = "empresa@x.com",
            PersonType = "juridical",
            ProcedureEntityId = BuyerEntityId,
            Metadata = "{\"representanteLegal\":{\"tipoDocumento\":\"CC\",\"numeroDocumento\":\"555\",\"nombreCompleto\":\"Rep Legal\",\"email\":\"rl-viejo@x.com\"}}",
        });
        // La validación previa quedó anclada al DOCUMENTO del RL (sujeto de identidad de la PJ).
        var previa = SentValidation("comprador", "CC", "555", "rl-viejo@x.com");
        instance.BiometricValidations.Add(previa);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        _providerOptions.Provider = BiometricProviders.Kyverum;
        StubKyverumOk();

        var rl = new ActorRepresentanteLegal("CC", "555", "Rep Legal", "rl-nuevo@x.com", null);
        var actor = new ActorInput("comprador", "NIT", "900123456", "ACME S.A.S.", "empresa@x.com", null,
            PersonType: "juridical", RepresentanteLegal: rl);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);

        error.Should().BeNull();
        result!.Actors[0].RepresentanteLegal!.Email.Should().Be("rl-nuevo@x.com");
        previa.Status.Should().Be(BiometricEstados.Expirado);
        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r => r.Email == "rl-nuevo@x.com" && r.Documento == "555"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_PersonaJuridica_CorreoEmpresaCambia_RLIgual_NoReenvia()
    {
        // PJ: si solo cambia el correo de la EMPRESA (no relevante para identidad) y el del RL se mantiene,
        // no debe reenviarse nada (el sujeto de identidad -RL- no cambió).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ActorType = "comprador",
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "ACME S.A.S.",
            Email = "empresa-vieja@x.com",
            PersonType = "juridical",
            ProcedureEntityId = BuyerEntityId,
            Metadata = "{\"representanteLegal\":{\"tipoDocumento\":\"CC\",\"numeroDocumento\":\"555\",\"nombreCompleto\":\"Rep Legal\",\"email\":\"rl@x.com\"}}",
        });
        var previa = SentValidation("comprador", "CC", "555", "rl@x.com");
        instance.BiometricValidations.Add(previa);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        _providerOptions.Provider = BiometricProviders.Kyverum;

        var rl = new ActorRepresentanteLegal("CC", "555", "Rep Legal", "rl@x.com", null);
        var actor = new ActorInput("comprador", "NIT", "900123456", "ACME S.A.S.", "empresa-nueva@x.com", null,
            PersonType: "juridical", RepresentanteLegal: rl);

        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);

        error.Should().BeNull();
        previa.Status.Should().Be(BiometricEstados.EnProceso); // sin cambios: RL (sujeto) igual.
        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }
}

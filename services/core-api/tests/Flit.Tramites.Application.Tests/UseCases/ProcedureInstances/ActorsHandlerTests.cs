using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class ActorsHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly PutActorsHandler _put;
    private readonly GetActorsHandler _get;

    private static readonly Guid BuyerEntityId = Guid.NewGuid();
    private static readonly Guid OwnerEntityId = Guid.NewGuid();

    public ActorsHandlerTests()
    {
        _put = new PutActorsHandler(_repo, _catalogRepo);
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
        string status = ProcedureInstanceStatus.Draft,
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
        _repo.GetByIdWithActorsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await _put.HandleAsync(Guid.NewGuid(), Guid.NewGuid(),
            new PutActorsRequest([Comprador()]), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("submitted")]
    [InlineData("completed")]
    public async Task Put_NotDraft_ReturnsConflict(string status)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, status: status));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([Comprador()]), ct);

        error.Should().Be("not_draft");
        result.Should().BeNull();
    }

    // ── Roles por modalidad ───────────────────────────────────────────────────

    [Fact]
    public async Task Put_MatriculaInicial_HappyPath_SavesComprador()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(instance);

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "matricula_inicial"));

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(instance);

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(instance);

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(instance);

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(instance);

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(instance);

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "traspaso"));

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, modalidad: "traspaso"));

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _put.HandleAsync(id, tenant,
            new PutActorsRequest([Vendedor(), Comprador(doc: "NEW")]), ct);

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
        _repo.GetByIdWithActorsAsync(id, tenant, ct).Returns(instance);

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
}

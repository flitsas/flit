using Flit.Modules.Improntas.Domain;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class GenerarImprontaAttachmentHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeImprontaExternalClient _client = new();
    private readonly FakeStorage _storage = new();
    private readonly GenerarImprontaAttachmentHandler _handler;

    public GenerarImprontaAttachmentHandlerTests()
    {
        var uploadHandler = new UploadAttachmentHandler(_repo, _storage);
        _handler = new GenerarImprontaAttachmentHandler(_repo, _client, uploadHandler);
    }

    private sealed class FakeImprontaExternalClient : IImprontaExternalClient
    {
        public ImprontaExternalRequest? LastRequest { get; private set; }
        public ImprontaRuntException? Throw { get; set; }

        public Task<ImprontaExternalResult> GenerarAsync(ImprontaExternalRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            if (Throw is not null)
                throw Throw;
            var dataUri = "data:application/pdf;base64," + Convert.ToBase64String("%PDF-1.4 fake"u8.ToArray());
            return Task.FromResult(new ImprontaExternalResult(dataUri, "hash-abc", "IMPR-TEST0001", "2026-07-03T10:00:00Z"));
        }
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Deleted { get; } = [];

        public Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            Task.FromResult(new StoredFile($"{procedureInstanceId:D}/{tipo}", "sha-impronta", 42));

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Deleted.Add(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string tipologia) =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For(tipologia ?? (tipologia == TramiteTipologiaCatalog.CodigoTraspasoStandard ? "traspaso" : "matricula_inicial")),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = tipologia == TramiteTipologiaCatalog.CodigoTraspasoStandard ? "traspaso" : "matricula_inicial",
            TipologiaCodigo = tipologia,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static void WithOrganismo(ProcedureInstance instance) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });

    private static void WithOrganismoNombre(ProcedureInstance instance, string nombre) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "transit_office_name",
            ValueText = nombre,
            Source = "user",
        });

    private static void WithPlaca(ProcedureInstance instance, string placa) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "plate",
            ValueText = placa,
            Source = "user",
        });

    private static void WithVin(ProcedureInstance instance, string vin) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "vin",
            ValueText = vin,
            Source = "user",
        });

    private static ProcedureInstanceActor Actor(Guid tenantId, Guid instanceId, string rol, string documento) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = rol,
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = "Persona de prueba",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private void SetupRepoGraphs(ProcedureInstance instance)
    {
        _repo.GetByIdWithFurGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
    }

    [Fact]
    public async Task Generar_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithFurGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Generar_MatriculaInicial_ConVin_UsaCompradorYVin()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        WithOrganismoNombre(instance, "SDM Bogotá");
        WithVin(instance, "1HGCM82633A004352");
        instance.Actors.Add(Actor(tenant, id, "comprador", "123456789"));
        SetupRepoGraphs(instance);
        _repo.GetUserDisplayNameAsync(requestedBy, ct).Returns("Ana Operadora");

        var (result, error) = await _handler.HandleAsync(id, tenant, requestedBy, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Radicado.Should().Be("IMPR-TEST0001");
        _client.LastRequest!.Placa.Should().BeNull();
        _client.LastRequest!.Vin.Should().Be("1HGCM82633A004352");
        _client.LastRequest!.Documento.Should().Be("123456789");
        _client.LastRequest!.OrgNombre.Should().Be("SDM Bogotá");
        _client.LastRequest!.Operador.Should().Be("Ana Operadora");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "impronta");
    }

    [Fact]
    public async Task Generar_MatriculaInicial_ConPlacaYVin_IgnoraPlacaYUsaVin()
    {
        // El RUNT puede devolver una placa ya asignada al consultar el VIN de matrícula inicial
        // (el organismo de tránsito la conoce de antemano), pero el radicador solo tiene certeza
        // del VIN grabado en el vehículo. Verificado contra el proveedor real: enviar placa+documento
        // sin relación entre sí devuelve ok:false; enviar vin+documento resuelve correctamente. Por
        // eso matrícula inicial NUNCA debe enviar placa, aunque el campo esté poblado.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        WithOrganismoNombre(instance, "SDM Bogotá");
        WithPlaca(instance, "QYQ132");
        WithVin(instance, "1HGCM82633A004352");
        instance.Actors.Add(Actor(tenant, id, "comprador", "123456789"));
        SetupRepoGraphs(instance);
        _repo.GetUserDisplayNameAsync(requestedBy, ct).Returns("Ana Operadora");

        var (result, error) = await _handler.HandleAsync(id, tenant, requestedBy, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        _client.LastRequest!.Placa.Should().BeNull();
        _client.LastRequest!.Vin.Should().Be("1HGCM82633A004352");
    }

    [Fact]
    public async Task Generar_Traspaso_ConPlaca_UsaDocumentoDelVendedorNoDelComprador()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        WithOrganismoNombre(instance, "SDM Bogotá");
        WithPlaca(instance, "abc123");
        instance.Actors.Add(Actor(tenant, id, "comprador", "111"));
        instance.Actors.Add(Actor(tenant, id, "vendedor", "222"));
        SetupRepoGraphs(instance);
        _repo.GetUserDisplayNameAsync(requestedBy, ct).Returns("Ana Operadora");

        var (result, error) = await _handler.HandleAsync(id, tenant, requestedBy, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        _client.LastRequest!.Placa.Should().Be("abc123");
        _client.LastRequest!.Vin.Should().BeNull();
        _client.LastRequest!.Documento.Should().Be("222");
    }

    [Fact]
    public async Task Generar_ImprontaYaExiste_Returns409SinLlamarAlProveedor()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        WithPlaca(instance, "abc123");
        instance.Actors.Add(Actor(tenant, id, "comprador", "123"));
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Tipo = "impronta",
            Filename = "impronta.pdf",
            Mimetype = "application/pdf",
            StoragePath = "existing/impronta",
            Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        SetupRepoGraphs(instance);

        var (_, error) = await _handler.HandleAsync(id, tenant, Guid.NewGuid(), ct);

        error.Should().Be("impronta_ya_existe");
        _client.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task Generar_OrganismoRequerido_SinTransitOfficeCode()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithPlaca(instance, "abc123");
        instance.Actors.Add(Actor(tenant, id, "comprador", "123"));
        SetupRepoGraphs(instance);

        var (_, error) = await _handler.HandleAsync(id, tenant, Guid.NewGuid(), ct);

        error.Should().Be("organismo_requerido");
        _client.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task Generar_IdentificadorVehiculoRequerido_SinPlacaNiVin()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        WithOrganismoNombre(instance, "SDM Bogotá");
        instance.Actors.Add(Actor(tenant, id, "comprador", "123"));
        SetupRepoGraphs(instance);

        var (_, error) = await _handler.HandleAsync(id, tenant, Guid.NewGuid(), ct);

        error.Should().Be("identificador_vehiculo_requerido");
        _client.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task Generar_DocumentoPropietarioRequerido_SinActorVendedorEnTraspaso()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        WithOrganismoNombre(instance, "SDM Bogotá");
        WithPlaca(instance, "abc123");
        instance.Actors.Add(Actor(tenant, id, "comprador", "111")); // falta vendedor
        SetupRepoGraphs(instance);

        var (_, error) = await _handler.HandleAsync(id, tenant, Guid.NewGuid(), ct);

        error.Should().Be("documento_propietario_requerido");
        _client.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task Generar_OperadorNoResuelto_SinDisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        WithOrganismoNombre(instance, "SDM Bogotá");
        WithVin(instance, "1HGCM82633A004352");
        instance.Actors.Add(Actor(tenant, id, "comprador", "123"));
        SetupRepoGraphs(instance);
        _repo.GetUserDisplayNameAsync(requestedBy, ct).Returns((string?)null);

        var (_, error) = await _handler.HandleAsync(id, tenant, requestedBy, ct);

        error.Should().Be("operador_no_resuelto");
        _client.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task Generar_NotDraft_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        instance.Status = TramiteEstado.Preparado;
        SetupRepoGraphs(instance);

        var (_, error) = await _handler.HandleAsync(id, tenant, Guid.NewGuid(), ct);

        error.Should().Be("not_draft");
    }

    [Fact]
    public async Task Generar_ProviderUnavailable_MapeaError()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        WithOrganismoNombre(instance, "SDM Bogotá");
        WithVin(instance, "1HGCM82633A004352");
        instance.Actors.Add(Actor(tenant, id, "comprador", "123"));
        SetupRepoGraphs(instance);
        _repo.GetUserDisplayNameAsync(requestedBy, ct).Returns("Ana Operadora");
        _client.Throw = new ImprontaRuntException("Kyverum RUNT no disponible temporalmente.", isTransient: true);

        var (_, error) = await _handler.HandleAsync(id, tenant, requestedBy, ct);

        error.Should().Be("provider_unavailable");
        instance.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task Generar_ChecklistImprontaSeAutoMarca()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        WithOrganismoNombre(instance, "SDM Bogotá");
        WithVin(instance, "1HGCM82633A004352");
        instance.Actors.Add(Actor(tenant, id, "comprador", "123"));
        SetupRepoGraphs(instance);
        _repo.GetUserDisplayNameAsync(requestedBy, ct).Returns("Ana Operadora");

        var (_, error) = await _handler.HandleAsync(id, tenant, requestedBy, ct);

        error.Should().BeNull();
        instance.ChecklistEstado.Should().Contain("\"impronta\":true");
    }
}

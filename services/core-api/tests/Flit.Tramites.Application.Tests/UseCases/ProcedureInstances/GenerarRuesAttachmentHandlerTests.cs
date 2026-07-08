using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;
// El tipo 'rues' vive en el catálogo (no en la whitelist legada), así que el UploadAttachmentHandler
// se construye con un AttachmentValidator respaldado por un catálogo que lo contiene (como en producción).

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// RF36 — autogeneración del Certificado RUES para actor NIT, con respaldo a carga manual cuando la
/// integración está deshabilitada (cliente externo en null). Espejo del handler de improntas.
/// </summary>
public sealed class GenerarRuesAttachmentHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeRuesExternalClient _client = new();
    private readonly FakeStorage _storage = new();

    private UploadAttachmentHandler Upload() =>
        new(_repo, _storage, new AttachmentValidator(new FakeCatalog(
            new DocumentTypeRule("rues", ["application/pdf", "image/jpeg", "image/png", "image/webp"], 20L * 1024 * 1024))));

    private GenerarRuesAttachmentHandler HandlerConCliente() => new(_repo, Upload(), _client);

    private GenerarRuesAttachmentHandler HandlerSinCliente() => new(_repo, Upload());

    private sealed class FakeCatalog(params DocumentTypeRule[] rules) : IDocumentTypeCatalog
    {
        private readonly Dictionary<string, DocumentTypeRule> _rules = rules.ToDictionary(r => r.Code, r => r);

        public Task<DocumentTypeRule?> GetRuleAsync(string tipo, CancellationToken ct = default) =>
            Task.FromResult(_rules.TryGetValue(tipo, out var r) ? r : null);
    }

    private sealed class FakeRuesExternalClient : IRuesExternalClient
    {
        public RuesExternalRequest? LastRequest { get; private set; }
        public RuesExternalException? Throw { get; set; }

        public Task<RuesExternalResult> GenerarAsync(RuesExternalRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            if (Throw is not null)
                throw Throw;
            var dataUri = "data:application/pdf;base64," + Convert.ToBase64String("%PDF-1.4 rues"u8.ToArray());
            return Task.FromResult(new RuesExternalResult(dataUri, "RUES-0001"));
        }
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            Task.FromResult(new StoredFile($"{procedureInstanceId:D}/{tipo}", "sha-rues", 42));

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) { }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);
    }

    private ProcedureInstance InstanceConNit(bool conActorNit = true, string status = TramiteEstado.Borrador)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000010",
            Status = status,
            ModalidadEntrada = "traspaso",
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoTraspasoStandard,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (conActorNit)
            instance.Actors.Add(new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                ProcedureEntityId = Guid.NewGuid(),
                ActorType = "comprador",
                DocumentType = "NIT",
                DocumentNumber = "900123456",
                FullName = "Empresa SAS",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        _repo.GetByIdWithFurGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    [Fact]
    public async Task SinCliente_AutogenDeshabilitado()
    {
        var instance = InstanceConNit();
        var (result, error) = await HandlerSinCliente()
            .HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be("rues_autogen_disabled");
        instance.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task SinActorNit_ActorNitRequerido()
    {
        var instance = InstanceConNit(conActorNit: false);
        var (result, error) = await HandlerConCliente()
            .HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be("actor_nit_requerido");
    }

    [Fact]
    public async Task NoBorrador_NotDraft()
    {
        var instance = InstanceConNit(status: TramiteEstado.Preparado);
        var (_, error) = await HandlerConCliente()
            .HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), TestContext.Current.CancellationToken);

        error.Should().Be("not_draft");
    }

    [Fact]
    public async Task RuesYaExiste_NoRegenera()
    {
        var instance = InstanceConNit();
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = "rues",
            Filename = "rues.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = "x",
            StoragePath = "p",
            Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        var (_, error) = await HandlerConCliente()
            .HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), TestContext.Current.CancellationToken);

        error.Should().Be("rues_ya_existe");
        _client.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task Autogenera_AdjuntaRues()
    {
        var instance = InstanceConNit();
        var (result, error) = await HandlerConCliente()
            .HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Radicado.Should().Be("RUES-0001");
        _client.LastRequest!.Nit.Should().Be("900123456");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "rues");
    }

    [Fact]
    public async Task ProveedorTransitorio_ProviderUnavailable()
    {
        var instance = InstanceConNit();
        _client.Throw = new RuesExternalException("caído", isTransient: true);

        var (result, error) = await HandlerConCliente()
            .HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be("provider_unavailable");
        instance.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task ProveedorNoTransitorio_ProviderError()
    {
        var instance = InstanceConNit();
        _client.Throw = new RuesExternalException("datos inválidos", isTransient: false);

        var (_, error) = await HandlerConCliente()
            .HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), TestContext.Current.CancellationToken);

        error.Should().Be("provider_error");
    }
}

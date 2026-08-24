using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class AttachmentPreviewUrlHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakePreviewStorage _storage = new();
    private readonly GetAttachmentPreviewUrlHandler _handler;

    public AttachmentPreviewUrlHandlerTests()
    {
        _handler = new GetAttachmentPreviewUrlHandler(_repo, _storage);
    }

    private sealed class FakePreviewStorage : IAttachmentStorage
    {
        public bool ReturnNull { get; set; }

        public Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) { }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default)
        {
            if (ReturnNull || string.IsNullOrWhiteSpace(storagePath))
                return Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
            return Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(
                ($"https://s3.test/view/{storagePath}", DateTimeOffset.UtcNow.AddMinutes(10)));
        }
    }

    private static ProcedureInstance InstanceWithAttachment(
        Guid instanceId, Guid tenantId, out Guid attachmentId, out string storagePath)
    {
        var attId = Guid.NewGuid();
        storagePath = $"{instanceId:D}/factura";
        attachmentId = attId;

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = instanceId,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = attId,
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            Tipo = "factura",
            Filename = "factura.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 1024,
            Sha256 = "deadbeef",
            StoragePath = storagePath,
            Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        return instance;
    }

    [Fact]
    public async Task HandleAsync_InstanceNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_AttachmentNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceWithAttachment(instanceId, tenantId, out _, out _);
        _repo.GetByIdWithAttachmentsAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await _handler.HandleAsync(instanceId, tenantId, Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_StorageUnavailable_ReturnsStorageUnavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceWithAttachment(instanceId, tenantId, out var attachmentId, out _);
        _repo.GetByIdWithAttachmentsAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _storage.ReturnNull = true;

        var (result, error) = await _handler.HandleAsync(instanceId, tenantId, attachmentId, ct);

        error.Should().Be("storage_unavailable");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_Happy_ReturnsUrlAndExpiresAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceWithAttachment(instanceId, tenantId, out var attachmentId, out var storagePath);
        _repo.GetByIdWithAttachmentsAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await _handler.HandleAsync(instanceId, tenantId, attachmentId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Url.Should().Contain(storagePath);
        result.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }
}

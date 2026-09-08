using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ImprintSignatures;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ImprintSignatures;

public sealed class GetImprintSignaturePreviewUrlHandlerTests
{
    private readonly IVehicleSignatureImprintRepository _imprints = Substitute.For<IVehicleSignatureImprintRepository>();
    private readonly IProcedureInstanceRepository _instances = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakePreviewStorage _storage = new();
    private readonly GetImprintSignaturePreviewUrlHandler _sut;

    public GetImprintSignaturePreviewUrlHandlerTests()
    {
        _sut = new GetImprintSignaturePreviewUrlHandler(_imprints, _instances, _storage);
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

    [Fact]
    public async Task HandleAsync_NotFound_WhenImprintMissing()
    {
        var id = Guid.NewGuid();
        _imprints.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((VehicleSignatureImprint?)null);

        var (result, error) = await _sut.HandleAsync(id, TestContext.Current.CancellationToken);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_UsesSignedStoragePath_WhenPresent()
    {
        var id = Guid.NewGuid();
        _imprints.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new VehicleSignatureImprint
        {
            Id = id,
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = Guid.NewGuid(),
            ModuleCode = "impronta_manual",
            DocumentHash = "hash",
            PublicKey = "pk",
            PrivateKey = "sk",
            Signature = "sig",
            SignedAt = DateTimeOffset.UtcNow,
            SignedStoragePath = "snap/impronta.pdf",
            AttachmentId = null,
        });

        var (result, error) = await _sut.HandleAsync(id, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Url.Should().Contain("snap/impronta.pdf");
        await _instances.DidNotReceive()
            .GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FallsBackToAttachment_WhenSnapshotMissing()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();

        _imprints.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new VehicleSignatureImprint
        {
            Id = id,
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            ModuleCode = "impronta_manual",
            DocumentHash = "hash",
            PublicKey = "pk",
            PrivateKey = "sk",
            Signature = "sig",
            SignedAt = DateTimeOffset.UtcNow,
            SignedStoragePath = null,
            AttachmentId = attachmentId,
        });

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = instanceId,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000099",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = attachmentId,
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            Tipo = "impronta",
            Filename = "impronta.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = "abc",
            StoragePath = "att/path.pdf",
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _instances.GetByIdWithAttachmentsAsync(instanceId, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Url.Should().Contain("att/path.pdf");
    }

    [Fact]
    public async Task HandleAsync_FileMissing_WhenNoPath()
    {
        var id = Guid.NewGuid();
        _imprints.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new VehicleSignatureImprint
        {
            Id = id,
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = Guid.NewGuid(),
            ModuleCode = "impronta_manual",
            DocumentHash = "hash",
            PublicKey = "pk",
            PrivateKey = "sk",
            Signature = "sig",
            SignedAt = DateTimeOffset.UtcNow,
            SignedStoragePath = null,
            AttachmentId = null,
        });

        var (result, error) = await _sut.HandleAsync(id, TestContext.Current.CancellationToken);

        error.Should().Be("file_missing");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_StorageUnavailable_WhenPresignFails()
    {
        var id = Guid.NewGuid();
        _storage.ReturnNull = true;
        _imprints.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new VehicleSignatureImprint
        {
            Id = id,
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = Guid.NewGuid(),
            ModuleCode = "impronta_manual",
            DocumentHash = "hash",
            PublicKey = "pk",
            PrivateKey = "sk",
            Signature = "sig",
            SignedAt = DateTimeOffset.UtcNow,
            SignedStoragePath = "snap/x.pdf",
        });

        var (result, error) = await _sut.HandleAsync(id, TestContext.Current.CancellationToken);

        error.Should().Be("storage_unavailable");
        result.Should().BeNull();
    }
}

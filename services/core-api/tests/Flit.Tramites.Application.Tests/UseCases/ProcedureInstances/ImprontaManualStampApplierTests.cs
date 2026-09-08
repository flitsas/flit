using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class ImprontaManualStampApplierTests
{
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly IImprontaManualStamper _stamper = Substitute.For<IImprontaManualStamper>();
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IVehicleSignatureImprintRepository _audit = Substitute.For<IVehicleSignatureImprintRepository>();

    private static ProcedureInstance Instance() =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FLIT-1",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard),
            Attachments = [],
        };

    private static ProcedureInstanceAttachment Attachment(Guid tenantId, Guid instanceId, string? provider) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            Tipo = "impronta",
            Filename = "impronta.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = "aa",
            StoragePath = "path-old",
            Source = "user",
            Provider = provider,
            UploadedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task MaybeStamp_Kyverum_DoesNotCallStamper()
    {
        var instance = Instance();
        var pdf = "%PDF-kyverum"u8.ToArray();
        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, Attachment(instance.TenantId, instance.Id, AttachmentProviders.Kyverum), instance,
            _storage, _stamper, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(pdf);
        _stamper.DidNotReceive().Stamp(Arg.Any<byte[]>(), Arg.Any<ImprontaManualStampContext>());
    }

    [Fact]
    public async Task MaybeStamp_Manual_CallsStamper_AndPersists()
    {
        var instance = Instance();
        var att = Attachment(instance.TenantId, instance.Id, null);
        instance.Attachments.Add(att);
        var pdf = "%PDF-manual"u8.ToArray();
        var stampedPdf = "%PDF-stamped"u8.ToArray();
        var stamp = new ImprontaManualStampResult(
            stampedPdf,
            Applied: true,
            DocumentHash: "abc123",
            SignatureBase64: "sig==",
            PrivateKeyPem: "-----BEGIN RSA PRIVATE KEY-----\nX\n-----END RSA PRIVATE KEY-----",
            PublicKeyPem: "-----BEGIN RSA PUBLIC KEY-----\nY\n-----END RSA PUBLIC KEY-----",
            SignedAt: DateTimeOffset.UtcNow,
            WasSignedWithoutOwnerSignature: true);
        _stamper.AlreadyStamped(pdf).Returns(false);
        _stamper.Stamp(pdf, Arg.Any<ImprontaManualStampContext>()).Returns(stamp);
        _audit.FindByDocumentHashAsync("abc123", Arg.Any<CancellationToken>()).Returns((VehicleSignatureImprint?)null);
        _storage.SaveAsync(instance.Id, "impronta", "impronta.pdf", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("path-new", "bb", 99));

        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, att, instance, _storage, _stamper, TestContext.Current.CancellationToken,
            repo: _repo, auditRepo: _audit);

        result.Should().BeSameAs(stampedPdf);
        _stamper.Received(1).Stamp(pdf, Arg.Any<ImprontaManualStampContext>());
        await _storage.Received(1).SaveAsync(
            instance.Id, "impronta", "impronta.pdf", Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        att.StoragePath.Should().Be("path-new");
        att.Sha256.Should().Be("bb");
        att.SizeBytes.Should().Be(99);
        _repo.DidNotReceive().RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>());
        _audit.Received(1).Add(Arg.Is<VehicleSignatureImprint>(r =>
            r.DocumentHash == "abc123"
            && r.AttachmentId == att.Id
            && r.Signature == "sig=="
            && r.PrivateKey.Contains("PRIVATE KEY", StringComparison.Ordinal)
            && r.WasSignedWithoutOwnerSignature));
    }

    [Fact]
    public async Task MaybeStamp_NonImpronta_Skips()
    {
        var instance = Instance();
        var pdf = "%PDF-x"u8.ToArray();
        var att = Attachment(instance.TenantId, instance.Id, null);
        att.Tipo = "soat";

        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, att, instance, _storage, _stamper, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(pdf);
        _stamper.DidNotReceive().Stamp(Arg.Any<byte[]>(), Arg.Any<ImprontaManualStampContext>());
    }

    [Fact]
    public async Task MaybeStamp_NullStamper_Passthrough()
    {
        var instance = Instance();
        var pdf = "%PDF-x"u8.ToArray();
        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, Attachment(instance.TenantId, instance.Id, null), instance, _storage, null,
            TestContext.Current.CancellationToken);
        result.Should().BeSameAs(pdf);
    }

    [Fact]
    public async Task MaybeStamp_AlreadyAuditedHash_DoesNotReupload()
    {
        var instance = Instance();
        var att = Attachment(instance.TenantId, instance.Id, null);
        instance.Attachments.Add(att);
        var pdf = "%PDF-manual"u8.ToArray();
        var stampedPdf = "%PDF-stamped"u8.ToArray();
        var stamp = new ImprontaManualStampResult(
            stampedPdf, Applied: true, DocumentHash: "dup", SignatureBase64: "s",
            PrivateKeyPem: "pk", PublicKeyPem: "pub", SignedAt: DateTimeOffset.UtcNow);
        _stamper.AlreadyStamped(pdf).Returns(false);
        _stamper.Stamp(pdf, Arg.Any<ImprontaManualStampContext>()).Returns(stamp);
        _audit.FindByDocumentHashAsync("dup", Arg.Any<CancellationToken>())
            .Returns(new VehicleSignatureImprint { DocumentHash = "dup" });

        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, att, instance, _storage, _stamper, TestContext.Current.CancellationToken,
            repo: _repo, auditRepo: _audit);

        result.Should().BeSameAs(stampedPdf);
        await _storage.DidNotReceive().SaveAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        _audit.DidNotReceive().Add(Arg.Any<VehicleSignatureImprint>());
    }
}

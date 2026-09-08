using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
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

    private static ProcedureInstance InstanceTraspasoReady()
    {
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FLIT-1",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard),
            Attachments = [],
        };
        AddOwnerWithIdentity(instance, "vendedor", "CC", "100");
        return instance;
    }

    private static void AddOwnerWithIdentity(
        ProcedureInstance instance, string rol, string docType, string docNumber)
    {
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = rol,
            DocumentType = docType,
            DocumentNumber = docNumber,
            FullName = "Propietario Test",
            Ordinal = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = rol,
            DocumentType = docType,
            DocumentNumber = docNumber,
            Name = "Propietario Test",
            Status = BiometricEstados.Aprobado,
            CreatedAt = DateTimeOffset.UtcNow,
            ValidatedAt = DateTimeOffset.UtcNow,
            ValidUntil = DateTimeOffset.UtcNow.AddDays(10),
        });
    }

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
        var instance = InstanceTraspasoReady();
        var pdf = "%PDF-kyverum"u8.ToArray();
        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, Attachment(instance.TenantId, instance.Id, AttachmentProviders.Kyverum), instance,
            _storage, _stamper, TestContext.Current.CancellationToken, repo: _repo);

        result.Should().BeSameAs(pdf);
        _stamper.DidNotReceive().Stamp(Arg.Any<byte[]>(), Arg.Any<ImprontaManualStampContext>());
    }

    [Fact]
    public async Task MaybeStamp_Manual_CallsStamper_AndPersists()
    {
        var instance = InstanceTraspasoReady();
        var att = Attachment(instance.TenantId, instance.Id, null);
        instance.Attachments.Add(att);
        var pdf = "%PDF-manual"u8.ToArray();
        var stampedPdf = "%PDF-stamped"u8.ToArray();
        var stamp = new ImprontaManualStampResult(
            stampedPdf,
            Applied: true,
            DocumentHash: "abc123",
            SignatureBase64: "sig==",
            PrivateKeyPem: "test-private-key",
            PublicKeyPem: "test-public-key",
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
            && r.PrivateKey == "test-private-key"
            && r.WasSignedWithoutOwnerSignature
            && r.SignedStoragePath == "path-new"
            && r.SignedSha256 == "bb"
            && r.SignedSizeBytes == 99
            && r.SignedFilename == "impronta.pdf"));
    }

    [Fact]
    public async Task MaybeStamp_MatriculaSinPlaca_DoesNotStamp()
    {
        var instance = InstanceTraspasoReady();
        instance.ProcedureType = ProcedureTypeFixture.Matricula;
        instance.Plate = null;
        instance.PlateFlowStatus = PlateFlowStatus.Preasignado;
        instance.Actors.Clear();
        instance.BiometricValidations.Clear();
        AddOwnerWithIdentity(instance, "comprador", "CC", "200");
        var pdf = "%PDF-m"u8.ToArray();
        var att = Attachment(instance.TenantId, instance.Id, null);

        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, att, instance, _storage, _stamper, TestContext.Current.CancellationToken, repo: _repo);

        result.Should().BeSameAs(pdf);
        _stamper.DidNotReceive().Stamp(Arg.Any<byte[]>(), Arg.Any<ImprontaManualStampContext>());
    }

    [Fact]
    public async Task MaybeStamp_SinIdentidadPropietarios_DoesNotStamp()
    {
        var instance = InstanceTraspasoReady();
        instance.BiometricValidations.Clear();
        var pdf = "%PDF-m"u8.ToArray();
        var att = Attachment(instance.TenantId, instance.Id, null);

        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, att, instance, _storage, _stamper, TestContext.Current.CancellationToken, repo: _repo);

        result.Should().BeSameAs(pdf);
        _stamper.DidNotReceive().Stamp(Arg.Any<byte[]>(), Arg.Any<ImprontaManualStampContext>());
    }

    [Fact]
    public async Task MaybeStamp_NonImpronta_Skips()
    {
        var instance = InstanceTraspasoReady();
        var pdf = "%PDF-x"u8.ToArray();
        var att = Attachment(instance.TenantId, instance.Id, null);
        att.Tipo = "soat";

        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, att, instance, _storage, _stamper, TestContext.Current.CancellationToken, repo: _repo);

        result.Should().BeSameAs(pdf);
        _stamper.DidNotReceive().Stamp(Arg.Any<byte[]>(), Arg.Any<ImprontaManualStampContext>());
    }

    [Fact]
    public async Task MaybeStamp_NullStamper_Passthrough()
    {
        var instance = InstanceTraspasoReady();
        var pdf = "%PDF-x"u8.ToArray();
        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, Attachment(instance.TenantId, instance.Id, null), instance, _storage, null,
            TestContext.Current.CancellationToken);
        result.Should().BeSameAs(pdf);
    }

    [Fact]
    public async Task MaybeStamp_AlreadyAuditedHash_DoesNotReupload()
    {
        var instance = InstanceTraspasoReady();
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

    [Fact]
    public async Task MaybeStamp_OnlySoftDeletedHash_PersistsNewAuditRow()
    {
        var instance = InstanceTraspasoReady();
        var att = Attachment(instance.TenantId, instance.Id, null);
        instance.Attachments.Add(att);
        var pdf = "%PDF-manual"u8.ToArray();
        var stampedPdf = "%PDF-stamped"u8.ToArray();
        var stamp = new ImprontaManualStampResult(
            stampedPdf,
            Applied: true,
            DocumentHash: "reused-after-soft-delete",
            SignatureBase64: "sig2==",
            PrivateKeyPem: "pk2",
            PublicKeyPem: "pub2",
            SignedAt: DateTimeOffset.UtcNow);
        _stamper.AlreadyStamped(pdf).Returns(false);
        _stamper.Stamp(pdf, Arg.Any<ImprontaManualStampContext>()).Returns(stamp);
        // Repo filtra soft-deleted: no hay fila activa con ese hash.
        _audit.FindByDocumentHashAsync("reused-after-soft-delete", Arg.Any<CancellationToken>())
            .Returns((VehicleSignatureImprint?)null);
        _storage.SaveAsync(instance.Id, "impronta", "impronta.pdf", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("path-restamp", "cc", 120));

        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, att, instance, _storage, _stamper, TestContext.Current.CancellationToken,
            repo: _repo, auditRepo: _audit);

        result.Should().BeSameAs(stampedPdf);
        _audit.Received(1).Add(Arg.Is<VehicleSignatureImprint>(r =>
            r.DocumentHash == "reused-after-soft-delete"
            && r.SignedStoragePath == "path-restamp"));
    }
}

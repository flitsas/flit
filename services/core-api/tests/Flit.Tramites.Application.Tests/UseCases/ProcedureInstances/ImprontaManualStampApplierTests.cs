using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
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
        };

    private static ProcedureInstanceAttachment Attachment(string? provider) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = Guid.NewGuid(),
            Tipo = "impronta",
            Filename = "impronta.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = "aa",
            StoragePath = "path",
            Source = "user",
            Provider = provider,
            UploadedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task MaybeStamp_Kyverum_DoesNotCallStamper()
    {
        var pdf = "%PDF-kyverum"u8.ToArray();
        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, Attachment(AttachmentProviders.Kyverum), Instance(), _storage, _stamper, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(pdf);
        _stamper.DidNotReceive().Stamp(Arg.Any<byte[]>(), Arg.Any<ImprontaManualStampContext>());
    }

    [Fact]
    public async Task MaybeStamp_Manual_CallsStamper()
    {
        var pdf = "%PDF-manual"u8.ToArray();
        var stamped = "%PDF-stamped"u8.ToArray();
        _stamper.AlreadyStamped(pdf).Returns(false);
        _stamper.Stamp(pdf, Arg.Any<ImprontaManualStampContext>()).Returns(stamped);

        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, Attachment(null), Instance(), _storage, _stamper, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(stamped);
        _stamper.Received(1).Stamp(pdf, Arg.Any<ImprontaManualStampContext>());
    }

    [Fact]
    public async Task MaybeStamp_NonImpronta_Skips()
    {
        var pdf = "%PDF-x"u8.ToArray();
        var att = Attachment(null);
        att.Tipo = "soat";

        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, att, Instance(), _storage, _stamper, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(pdf);
        _stamper.DidNotReceive().Stamp(Arg.Any<byte[]>(), Arg.Any<ImprontaManualStampContext>());
    }

    [Fact]
    public async Task MaybeStamp_NullStamper_Passthrough()
    {
        var pdf = "%PDF-x"u8.ToArray();
        var result = await ImprontaManualStampApplier.MaybeStampAsync(
            pdf, Attachment(null), Instance(), _storage, null, TestContext.Current.CancellationToken);
        result.Should().BeSameAs(pdf);
    }
}

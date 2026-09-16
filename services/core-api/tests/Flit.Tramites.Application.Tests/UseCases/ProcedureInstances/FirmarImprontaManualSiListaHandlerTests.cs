using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12116 — <see cref="FirmarImprontaManualSiListaHandler"/>: firma automática de la impronta
/// manual sin depender de que alguien genere el consolidado.
///
/// Uso de ejemplo:
/// <code>
/// var handler = new FirmarImprontaManualSiListaHandler(repo, merger, storage, stamper);
/// var resultado = await handler.HandleAsync(instanceId, tenantId, FirmaImprontaAutomaticaOrigen.Radicacion, ct);
/// </code>
/// </summary>
public sealed class FirmarImprontaManualSiListaHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IExpedienteConsolidadoMerger _merger = Substitute.For<IExpedienteConsolidadoMerger>();
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly IImprontaManualStamper _stamper = Substitute.For<IImprontaManualStamper>();
    private readonly IVehicleSignatureImprintRepository _auditRepo = Substitute.For<IVehicleSignatureImprintRepository>();
    private readonly IRegeneracionDocumentalTrazaWriter _traza = Substitute.For<IRegeneracionDocumentalTrazaWriter>();

    private FirmarImprontaManualSiListaHandler Handler() => new(
        _repo, _merger, _storage, _stamper, auditRepo: _auditRepo, trazaWriter: _traza);

    private static ProcedureInstance InstanceTraspasoReady(Guid id, Guid tenantId)
    {
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FLIT-1",
            Status = TramiteEstado.Entregado,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcedureType = ProcedureTypeFixture.Traspaso,
            Attachments = [],
        };
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "vendedor",
            DocumentType = "CC",
            DocumentNumber = "100",
            FullName = "Propietario Test",
            Ordinal = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            PartyRole = "vendedor",
            DocumentType = "CC",
            DocumentNumber = "100",
            Name = "Propietario Test",
            Status = BiometricEstados.Aprobado,
            CreatedAt = DateTimeOffset.UtcNow,
            ValidatedAt = DateTimeOffset.UtcNow,
            ValidUntil = DateTimeOffset.UtcNow.AddDays(10),
        });
        return instance;
    }

    private static ProcedureInstanceAttachment ImprontaAttachment(Guid tenantId, Guid instanceId) => new()
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
        UploadedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task HandleAsync_TramiteListo_Firma_YPersiste()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceTraspasoReady(instanceId, tenantId);
        var attachment = ImprontaAttachment(tenantId, instanceId);
        instance.Attachments.Add(attachment);
        _repo.GetByIdWithFurGraphAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var rawBytes = "%PDF-raw"u8.ToArray();
        var normalizedPdf = "%PDF-normalized"u8.ToArray();
        var stampedPdf = "%PDF-stamped"u8.ToArray();
        _storage.OpenReadAsync(attachment.StoragePath, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(rawBytes));
        _merger.NormalizeToPdf(Arg.Any<byte[]>(), attachment.Mimetype).Returns(normalizedPdf);
        _stamper.AlreadyStamped(normalizedPdf).Returns(false);
        var stamp = new ImprontaManualStampResult(
            stampedPdf, Applied: true, DocumentHash: "hash-1", SignatureBase64: "sig",
            PrivateKeyPem: "pk", PublicKeyPem: "pub", SignedAt: DateTimeOffset.UtcNow);
        _stamper.Stamp(normalizedPdf, Arg.Any<ImprontaManualStampContext>()).Returns(stamp);
        _auditRepo.FindActiveByInstanceAndHashAsync(instanceId, "hash-1", Arg.Any<CancellationToken>())
            .Returns((VehicleSignatureImprint?)null);
        _storage.SaveAsync(instanceId, "impronta", "impronta.pdf", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("path-new", "bb", 99));

        var resultado = await Handler().HandleAsync(
            instanceId, tenantId, FirmaImprontaAutomaticaOrigen.Radicacion, TestContext.Current.CancellationToken);

        resultado.Estado.Should().Be(FirmaImprontaAutomaticaEstado.Firmada);
        _auditRepo.Received(1).Add(Arg.Is<VehicleSignatureImprint>(r => r.DocumentHash == "hash-1"));
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _traza.DidNotReceive().EscribirFalloAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_YaFirmada_NoReupload()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceTraspasoReady(instanceId, tenantId);
        var attachment = ImprontaAttachment(tenantId, instanceId);
        instance.Attachments.Add(attachment);
        _repo.GetByIdWithFurGraphAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var normalizedPdf = "%PDF-normalized"u8.ToArray();
        _storage.OpenReadAsync(attachment.StoragePath, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream("%PDF-raw"u8.ToArray()));
        _merger.NormalizeToPdf(Arg.Any<byte[]>(), attachment.Mimetype).Returns(normalizedPdf);
        _stamper.AlreadyStamped(normalizedPdf).Returns(true);

        var resultado = await Handler().HandleAsync(
            instanceId, tenantId, FirmaImprontaAutomaticaOrigen.Radicacion, TestContext.Current.CancellationToken);

        resultado.Estado.Should().Be(FirmaImprontaAutomaticaEstado.YaFirmada);
        _stamper.DidNotReceive().Stamp(Arg.Any<byte[]>(), Arg.Any<ImprontaManualStampContext>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MatriculaPreasignada_NoLista_ConMotivoPlacaPreasignado()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceTraspasoReady(instanceId, tenantId);
        instance.ProcedureType = ProcedureTypeFixture.Matricula;
        instance.Plate = "ABC123";
        instance.PlateFlowStatus = Flit.Tramites.Domain.Tramites.Estados.PlateFlowStatus.Preasignado;
        var attachment = ImprontaAttachment(tenantId, instanceId);
        instance.Attachments.Add(attachment);
        _repo.GetByIdWithFurGraphAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _storage.OpenReadAsync(attachment.StoragePath, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream("%PDF-raw"u8.ToArray()));
        _merger.NormalizeToPdf(Arg.Any<byte[]>(), attachment.Mimetype).Returns("%PDF-normalized"u8.ToArray());
        _stamper.AlreadyStamped(Arg.Any<byte[]>()).Returns(false);

        var resultado = await Handler().HandleAsync(
            instanceId, tenantId, FirmaImprontaAutomaticaOrigen.AsignacionPlaca, TestContext.Current.CancellationToken);

        resultado.Estado.Should().Be(FirmaImprontaAutomaticaEstado.NoLista);
        resultado.Motivo.Should().Be(ImprontaManualStampReadiness.PlacaPreasignado);
    }

    [Fact]
    public async Task HandleAsync_SinAdjuntoImpronta_SinImprontaManual()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceTraspasoReady(instanceId, tenantId);
        _repo.GetByIdWithFurGraphAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var resultado = await Handler().HandleAsync(
            instanceId, tenantId, FirmaImprontaAutomaticaOrigen.Radicacion, TestContext.Current.CancellationToken);

        resultado.Estado.Should().Be(FirmaImprontaAutomaticaEstado.SinImprontaManual);
    }

    [Fact]
    public async Task HandleAsync_TramiteNoEncontrado_Fallo_YEscribeTraza()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithFurGraphAsync(instanceId, tenantId, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);

        var resultado = await Handler().HandleAsync(
            instanceId, tenantId, FirmaImprontaAutomaticaOrigen.Backfill, TestContext.Current.CancellationToken);

        resultado.Estado.Should().Be(FirmaImprontaAutomaticaEstado.Fallo);
        resultado.Motivo.Should().Be("tramite_no_encontrado");
        await _traza.Received(1).EscribirFalloAsync(
            tenantId, instanceId, FirmaImprontaAutomaticaOrigen.Backfill, "tramite_no_encontrado", null,
            FirmarImprontaManualSiListaHandler.EventoFallo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_StorageDevuelveExcepcion_NoLanza_QuedaComoFallo()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceTraspasoReady(instanceId, tenantId);
        var attachment = ImprontaAttachment(tenantId, instanceId);
        instance.Attachments.Add(attachment);
        _repo.GetByIdWithFurGraphAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _storage.OpenReadAsync(attachment.StoragePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Stream?>(new IOException("disco lleno")));

        var resultado = await Handler().HandleAsync(
            instanceId, tenantId, FirmaImprontaAutomaticaOrigen.Radicacion, TestContext.Current.CancellationToken);

        resultado.Estado.Should().Be(FirmaImprontaAutomaticaEstado.Fallo);
        resultado.Motivo.Should().Be("excepcion");
    }
}

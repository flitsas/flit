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
/// HU #12116 — <see cref="BackfillFirmaImprontaManualHandler"/>: procesa candidatos y clasifica el
/// resultado por categoría. Reutiliza el <see cref="FirmarImprontaManualSiListaHandler"/> REAL (no un
/// doble) para que el conteo refleje exactamente lo que el handler decide.
///
/// Uso de ejemplo:
/// <code>
/// var handler = new BackfillFirmaImprontaManualHandler(repo, firmaImpronta);
/// var resultado = await handler.HandleAsync(limit: 200, ct);
/// </code>
/// </summary>
public sealed class BackfillFirmaImprontaManualHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IExpedienteConsolidadoMerger _merger = Substitute.For<IExpedienteConsolidadoMerger>();
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly IImprontaManualStamper _stamper = Substitute.For<IImprontaManualStamper>();

    private static ProcedureInstance InstanceListaParaFirmar(Guid id, Guid tenantId)
    {
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = $"FLIT-{id.ToString("N")[..6]}",
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
    public async Task HandleAsync_ClasificaCadaCandidatoPorCategoria()
    {
        var tenantId = Guid.NewGuid();

        // Setup común: todo trámite candidato normaliza al MISMO pdf de referencia (el mimetype/
        // storage path son iguales entre attachments, así que se comparte un único stub).
        var normalizedPdf = "%PDF-normalized"u8.ToArray();
        _storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream("%PDF-raw"u8.ToArray()));
        _merger.NormalizeToPdf(Arg.Any<byte[]>(), Arg.Any<string>()).Returns(normalizedPdf);
        _stamper.AlreadyStamped(normalizedPdf).Returns(false);

        // Candidato 1: se firma.
        var idFirmable = Guid.NewGuid();
        var instanceFirmable = InstanceListaParaFirmar(idFirmable, tenantId);
        var attFirmable = ImprontaAttachment(tenantId, idFirmable);
        instanceFirmable.Attachments.Add(attFirmable);
        _repo.GetByIdWithFurGraphAsync(idFirmable, tenantId, Arg.Any<CancellationToken>()).Returns(instanceFirmable);
        _stamper.Stamp(normalizedPdf, Arg.Any<ImprontaManualStampContext>())
            .Returns(new ImprontaManualStampResult(
                "%PDF-stamped-1"u8.ToArray(), Applied: true, DocumentHash: "hash-1", SignatureBase64: "s",
                PrivateKeyPem: "pk", PublicKeyPem: "pub", SignedAt: DateTimeOffset.UtcNow));
        _storage.SaveAsync(idFirmable, "impronta", "impronta.pdf", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("path-new-1", "bb1", 10));

        // Candidato 2: sin adjunto de impronta (SinImprontaManual).
        var idSinImpronta = Guid.NewGuid();
        var instanceSinImpronta = InstanceListaParaFirmar(idSinImpronta, tenantId);
        _repo.GetByIdWithFurGraphAsync(idSinImpronta, tenantId, Arg.Any<CancellationToken>())
            .Returns(instanceSinImpronta);

        // Candidato 3: matrícula preasignada (NoLista).
        var idNoLista = Guid.NewGuid();
        var instanceNoLista = InstanceListaParaFirmar(idNoLista, tenantId);
        instanceNoLista.ProcedureType = ProcedureTypeFixture.Matricula;
        instanceNoLista.Plate = "ABC123";
        instanceNoLista.Status = TramiteEstado.Preasignacion; // ADR-0059: la preasignación es un estado del trámite
        var attNoLista = ImprontaAttachment(tenantId, idNoLista);
        instanceNoLista.Attachments.Add(attNoLista);
        _repo.GetByIdWithFurGraphAsync(idNoLista, tenantId, Arg.Any<CancellationToken>()).Returns(instanceNoLista);

        // Candidato 4: trámite no encontrado (Fallo).
        var idFallo = Guid.NewGuid();
        _repo.GetByIdWithFurGraphAsync(idFallo, tenantId, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);

        _repo.ListImprontaManualBackfillCandidatesAsync(200, Arg.Any<CancellationToken>())
            .Returns(new List<(Guid, Guid)>
            {
                (idFirmable, tenantId), (idSinImpronta, tenantId), (idNoLista, tenantId), (idFallo, tenantId),
            });

        var firmaImpronta = new FirmarImprontaManualSiListaHandler(_repo, _merger, _storage, _stamper);
        var handler = new BackfillFirmaImprontaManualHandler(_repo, firmaImpronta);

        var resultado = await handler.HandleAsync(200, TestContext.Current.CancellationToken);

        resultado.Total.Should().Be(4);
        resultado.Firmadas.Should().Be(1);
        resultado.SinImprontaManual.Should().Be(1);
        resultado.Fallidas.Should().Be(1);
        resultado.NoListas.Should().ContainKey(ImprontaManualStampReadiness.PlacaPreasignado)
            .WhoseValue.Should().Be(1);
        resultado.Limit.Should().Be(200);
    }

    [Fact]
    public async Task HandleAsync_SinCandidatos_DevuelveTodoEnCero()
    {
        _repo.ListImprontaManualBackfillCandidatesAsync(50, Arg.Any<CancellationToken>())
            .Returns(new List<(Guid, Guid)>());
        var firmaImpronta = new FirmarImprontaManualSiListaHandler(_repo, _merger, _storage, _stamper);
        var handler = new BackfillFirmaImprontaManualHandler(_repo, firmaImpronta);

        var resultado = await handler.HandleAsync(50, TestContext.Current.CancellationToken);

        resultado.Total.Should().Be(0);
        resultado.Firmadas.Should().Be(0);
        resultado.Fallidas.Should().Be(0);
        resultado.NoListas.Should().BeEmpty();
    }
}

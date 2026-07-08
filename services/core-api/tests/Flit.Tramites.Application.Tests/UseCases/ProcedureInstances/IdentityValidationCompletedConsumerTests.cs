using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Signatures;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>HU #10349 — AC4/AC5: consumidor de IdentityValidationCompleted (encadenado firma/FUR, multi-trámite, idempotencia).</summary>
public sealed class IdentityValidationCompletedConsumerTests
{
    private const string TipoDoc = "CC";
    private const string Documento = "1020304050";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly SolicitarFirmaHandler _firma;
    private readonly GenerarFurHandler _fur;
    private readonly IdentityValidationCompletedConsumer _sut;

    public IdentityValidationCompletedConsumerTests()
    {
        _firma = new SolicitarFirmaHandler(_repo, new MockSignatureProvider());
        _fur = new GenerarFurHandler(
            _repo, new MockFurDocumentGenerator(), Substitute.For<IKyverumCertificateClient>(),
            Substitute.For<IRuesCertificateGenerator>(), Substitute.For<IProcedureInstancePrendaRepository>(),
            new FakeStorage(), NullLogger<GenerarFurHandler>.Instance);
        _sut = new IdentityValidationCompletedConsumer(_repo, _firma, _fur);
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            return new StoredFile($"{procedureInstanceId:D}/{tipo}", $"sha-{tipo}", ms.Length);
        }

        public void Delete(string storagePath) { }
        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);
    }

    private ProcedureInstanceBiometricValidation AprobadaValidation(Guid validationId, Guid tenant, string parte = "comprador")
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = validationId,
            TenantId = tenant,
            ProcedureInstanceId = Guid.NewGuid(),
            PartyRole = parte,
            Name = "Ana Compradora",
            DocumentType = TipoDoc,
            DocumentNumber = Documento,
            Email = "ana@x.com",
            Status = BiometricEstados.Aprobado,
            Provider = BiometricProviders.Kyverum,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetBiometricByIdAsync(validationId, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    private static IdentityValidationCompleted Event(Guid validationId, Guid tenant, string parte = "comprador",
        string estado = BiometricEstados.Aprobado) =>
        new()
        {
            TenantId = tenant,
            ProcedureInstanceId = Guid.NewGuid(),
            ValidationId = validationId,
            Provider = BiometricProviders.Kyverum,
            Parte = parte,
            Estado = estado,
            Score = 95,
        };

    /// <summary>Instancia "lean" como la devuelve ListDraftFinalizedByActorAsync (id + modalidad + ref).</summary>
    private static ProcedureInstance Lean(Guid id, Guid tenant, string reference, bool traspaso)
    {
        var i = new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = reference,
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = traspaso ? "traspaso" : "matricula_inicial",
            TipologiaCodigo = traspaso ? TramiteTipologiaCatalog.CodigoTraspasoStandard : TramiteTipologiaCatalog.CodigoMatriculaInicial,
            DraftFinalizedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        return i;
    }

    /// <summary>Grafo completo de traspaso que recarga SolicitarFirmaHandler (GetByIdWithFurGraphAsync).</summary>
    private ProcedureInstance StubTraspasoGraph(Guid id, Guid tenant, ProcedureInstanceSignature? existing = null)
    {
        var i = Lean(id, tenant, "TRM-2026-000099", traspaso: true);
        i.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = TipoDoc,
            DocumentNumber = Documento,
            FullName = "Ana",
            Metadata = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        if (existing is not null)
            i.Signatures.Add(existing);
        _repo.GetByIdWithFurGraphAsync(id, tenant, Arg.Any<CancellationToken>()).Returns(i);
        return i;
    }

    /// <summary>Grafo completo de matrícula apto para GenerarFur (biométrica comprador aprobada + organismo).</summary>
    private ProcedureInstance StubMatriculaGraph(Guid id, Guid tenant)
    {
        var i = Lean(id, tenant, "TRM-2026-000050", traspaso: false);
        i.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = TipoDoc,
            DocumentNumber = Documento,
            FullName = "Ana",
            Metadata = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        i.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            PartyRole = "comprador",
            Status = BiometricEstados.Aprobado,
            Name = "Ana",
            DocumentType = TipoDoc,
            DocumentNumber = Documento,
            Email = "ana@x.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        i.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, Arg.Any<CancellationToken>()).Returns(i);
        return i;
    }

    // ── AC4/AC5 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Aprobado_Traspaso_MultiTramite_RequestsFirmaPerInstanceInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        AprobadaValidation(validationId, tenant);

        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        // Orden determinista garantizado por el repo: A (más antiguo) antes que B.
        _repo.ListDraftFinalizedByActorAsync(tenant, "comprador", TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance> { Lean(idA, tenant, "TRM-2026-000001", true), Lean(idB, tenant, "TRM-2026-000002", true) });
        var fullA = StubTraspasoGraph(idA, tenant);
        var fullB = StubTraspasoGraph(idB, tenant);

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Matched.Should().Be(2);
        result.Processed.Should().Be(2);
        fullA.Signatures.Should().ContainSingle(s => s.DocTipo == SignatureDocTipos.Compraventa && s.Parte == "comprador");
        fullB.Signatures.Should().ContainSingle(s => s.DocTipo == SignatureDocTipos.Compraventa);
        // Bitácora firma_auto_solicitada (una por trámite).
        _repo.Received(2).Add(Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "firma_auto_solicitada"));
        // Orden de procesamiento (AC5): A antes que B.
        Received.InOrder(() =>
        {
            _repo.GetByIdWithFurGraphAsync(idA, tenant, ct);
            _repo.GetByIdWithFurGraphAsync(idB, tenant, ct);
        });
    }

    [Fact]
    public async Task Aprobado_Traspaso_Idempotent_DoesNotDuplicateSignature()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        AprobadaValidation(validationId, tenant);

        var id = Guid.NewGuid();
        _repo.ListDraftFinalizedByActorAsync(tenant, "comprador", TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance> { Lean(id, tenant, "TRM-2026-000001", true) });
        // Ya existe una firma activa de compraventa para (comprador) → debe reusarse.
        var existing = new ProcedureInstanceSignature
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Parte = "comprador",
            DocTipo = SignatureDocTipos.Compraventa,
            Estado = SignatureEstados.Enviada,
            EnvelopeId = "env-1",
            SolicitadoAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var full = StubTraspasoGraph(id, tenant, existing);

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Processed.Should().Be(1);
        full.Signatures.Should().ContainSingle(); // sin duplicar
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceSignature>());
    }

    [Fact]
    public async Task Aprobado_Matricula_ChainsFurWithoutSignature()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        AprobadaValidation(validationId, tenant);

        var id = Guid.NewGuid();
        _repo.ListDraftFinalizedByActorAsync(tenant, "comprador", TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance> { Lean(id, tenant, "TRM-2026-000001", false) });
        var full = StubMatriculaGraph(id, tenant);

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Processed.Should().Be(1);
        full.Attachments.Should().Contain(a => a.Tipo == "fur");
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceSignature>());
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "firma_auto_solicitada"));
    }

    [Fact]
    public async Task NotApproved_IsNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();

        var result = await _sut.HandleAsync(Event(validationId, tenant, estado: BiometricEstados.Rechazado), ct);

        result.Matched.Should().Be(0);
        result.Processed.Should().Be(0);
        await _repo.DidNotReceive().GetBiometricByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidationNotFound_IsNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        _repo.GetBiometricByIdAsync(validationId, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Matched.Should().Be(0);
        await _repo.DidNotReceive().ListDraftFinalizedByActorAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Aprobado_NoMatchingDrafts_IsNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        AprobadaValidation(validationId, tenant);
        _repo.ListDraftFinalizedByActorAsync(tenant, "comprador", TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance>());

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Matched.Should().Be(0);
        result.Processed.Should().Be(0);
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceSignature>());
    }
}

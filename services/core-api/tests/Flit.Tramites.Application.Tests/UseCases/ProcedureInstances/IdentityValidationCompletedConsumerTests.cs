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
            Substitute.For<IRuesCertificateGenerator>(), Substitute.For<IRnmcCertificateGenerator>(),
            Substitute.For<IProcedureInstancePrendaRepository>(),
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

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
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

    /// <summary>Instancia "lean" como la devuelve ListPendientesDeFirmaPorSujetoAsync (id + modalidad + ref).</summary>
    private static ProcedureInstance Lean(Guid id, Guid tenant, string reference, bool traspaso)
    {
        var i = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(traspaso ? TramiteTipologiaCatalog.CodigoTraspasoStandard : TramiteTipologiaCatalog.CodigoMatriculaInicial ?? (traspaso ? "traspaso" : "matricula_inicial")),
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = reference,
            Status = TramiteEstado.Borrador,
            DraftFinalizedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // Review PR #510 (L2) — sin actor del sujeto el consumidor ya no cae a la parte validada: el
        // repositorio real trae los actores, así que el doble también.
        return ConSujeto(i, "comprador");
    }

    /// <summary>Agrega el actor cuyo sujeto de identidad es la persona validada (TipoDoc/Documento).</summary>
    private static ProcedureInstance ConSujeto(ProcedureInstance i, string parte, string documento = Documento)
    {
        i.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = i.TenantId,
            ProcedureInstanceId = i.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = parte,
            DocumentType = TipoDoc,
            DocumentNumber = documento,
            FullName = "Sujeto",
            Metadata = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
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
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
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
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
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
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance> { Lean(id, tenant, "TRM-2026-000001", false) });
        var full = StubMatriculaGraph(id, tenant);

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Processed.Should().Be(1);
        full.Attachments.Should().Contain(a => a.Tipo == "fur");
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceSignature>());
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "firma_auto_solicitada"));
    }


    /// <summary>
    /// ADR-0051 — el encadenamiento lo decide `generatesSaleDocument`, no la familia. Un
    /// TRASPASO_UNILATERAL es de familia TRASPASO y NO autogenera compraventa: con el criterio
    /// anterior, este consumidor pedía la firma de un documento que `FurCommand` ya no genera, el
    /// handler devolvía error y el borrador se quedaba SIN FUR, contado como `skipped`.
    /// </summary>
    [Fact]
    public async Task Aprobado_TraspasoUnilateral_EncadenaFur_NoFirmaDeCompraventa()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        // En unilateral el que valida identidad es el PROPIETARIO, no el comprador.
        AprobadaValidation(validationId, tenant, "vendedor");

        var id = Guid.NewGuid();
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance> { LeanUnilateral(id, tenant) });
        var full = StubUnilateralGraph(id, tenant);

        var result = await _sut.HandleAsync(Event(validationId, tenant, "vendedor"), ct);

        result.Processed.Should().Be(1);
        result.Skipped.Should().BeEmpty();
        full.Attachments.Should().Contain(a => a.Tipo == "fur");
        full.Signatures.Should().BeEmpty();
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceSignature>());
    }

    /// <summary>Instancia "lean" de TRASPASO_UNILATERAL (familia TRASPASO, sin compraventa).</summary>
    private static ProcedureInstance LeanUnilateral(Guid id, Guid tenant) =>
        ConSujeto(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.TraspasoUnilateral,
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = ProcedureTypeFixture.TraspasoUnilateral.Id,
            ReferenceNumber = "TRM-2026-000077",
            Status = TramiteEstado.Borrador,
            DraftFinalizedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        }, "vendedor");

    // ── Review PR #510 (MAYOR-2 acotado) ─────────────────────────────────────────────────────────

    /// <summary>Savepoint de prueba: ejecuta la operación y deja propagar la excepción, como el real.</summary>
    private sealed class SavepointDirecto : ISavepointScope
    {
        public int Llamadas { get; private set; }

        public Task<T> EjecutarAsync<T>(Func<Task<T>> operacion, CancellationToken ct = default)
        {
            Llamadas++;
            return operacion();
        }
    }

    [Fact]
    public async Task MAYOR2_i_ExcepcionEnUnTramite_NoCortaElLote()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        AprobadaValidation(validationId, tenant);

        var roto = Guid.NewGuid();
        var sano = Guid.NewGuid();
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance>
            {
                Lean(roto, tenant, "TRM-2026-000201", false),
                Lean(sano, tenant, "TRM-2026-000202", false),
            });
        _repo.GetByIdWithFurGraphAsync(roto, tenant, Arg.Any<CancellationToken>())
            .Returns<ProcedureInstance?>(_ => throw new InvalidOperationException("fallo de prueba"));
        var full = StubMatriculaGraph(sano, tenant);
        var savepoints = new SavepointDirecto();
        var sut = new IdentityValidationCompletedConsumer(_repo, _firma, _fur, savepoints);

        var result = await sut.HandleAsync(Event(validationId, tenant), ct);

        result.Processed.Should().Be(1, "el trámite sano se firma aunque el anterior haya fallado");
        result.Skipped.Should().ContainSingle(s => s == "TRM-2026-000201:" + IdentityValidationCompletedConsumer.OmitidoExcepcion);
        full.Attachments.Should().Contain(a => a.Tipo == "fur");
        savepoints.Llamadas.Should().Be(2, "un savepoint por trámite");
    }

    [Fact]
    public async Task MAYOR2_ii_FurVigenteGeneradoTrasLaAprobacion_SeOmiteComoYaFirmado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        var validacion = AprobadaValidation(validationId, tenant);
        validacion.ValidatedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        var id = Guid.NewGuid();
        var lean = Lean(id, tenant, "TRM-2026-000203", false);
        lean.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Tipo = "fur",
            Source = "system",
            StoragePath = "p/fur",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance> { lean });

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Processed.Should().Be(0);
        result.Skipped.Should().ContainSingle(s => s == "TRM-2026-000203:" + IdentityValidationCompletedConsumer.OmitidoYaFirmado);
        await _repo.DidNotReceive().GetByIdWithFurGraphAsync(id, tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MAYOR2_ii_FurAnteriorALaAprobacion_SeRegenera()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        var validacion = AprobadaValidation(validationId, tenant);
        validacion.ValidatedAt = DateTimeOffset.UtcNow;

        var id = Guid.NewGuid();
        var lean = Lean(id, tenant, "TRM-2026-000204", false);
        lean.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Tipo = "fur",
            Source = "system",
            StoragePath = "p/fur-viejo",
            UploadedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance> { lean });
        StubMatriculaGraph(id, tenant);

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Processed.Should().Be(1, "un FUR previo a la aprobación no lleva el sello de esta validación");
    }

    [Fact]
    public async Task L2_iii_SujetoQueNoEsParte_SeOmite_SinCaerALaParteValidada()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        AprobadaValidation(validationId, tenant);

        var id = Guid.NewGuid();
        var otro = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoMatriculaInicial ?? "matricula_inicial"),
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000205",
            Status = TramiteEstado.Borrador,
            DraftFinalizedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ConSujeto(otro, "comprador", documento: "9000000999"); // otra persona en la parte
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance> { otro });

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Processed.Should().Be(0);
        result.Skipped.Should().ContainSingle(s => s == "TRM-2026-000205:" + IdentityValidationCompletedConsumer.OmitidoSujetoNoEsParte);
        await _repo.DidNotReceive().GetByIdWithFurGraphAsync(id, tenant, Arg.Any<CancellationToken>());
    }

    /// <summary>Grafo apto para generar FUR: identidad del propietario aprobada y organismo resuelto.</summary>
    private ProcedureInstance StubUnilateralGraph(Guid id, Guid tenant)
    {
        var i = LeanUnilateral(id, tenant);
        i.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "vendedor",
            DocumentType = TipoDoc,
            DocumentNumber = Documento,
            FullName = "Leasing S.A.",
            Metadata = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        i.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            PartyRole = "vendedor",
            Status = BiometricEstados.Aprobado,
            Name = "Leasing S.A.",
            DocumentType = TipoDoc,
            DocumentNumber = Documento,
            Email = "leasing@x.com",
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
        await _repo.DidNotReceive().ListPendientesDeFirmaPorSujetoAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Aprobado_NoMatchingDrafts_IsNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        AprobadaValidation(validationId, tenant);
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance>());

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Matched.Should().Be(0);
        result.Processed.Should().Be(0);
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceSignature>());
    }

    // ── Bug #13194 (punto 4) — fan-out por sujeto, estados pendientes e idempotencia ─────────────

    /// <summary>Traspaso en <paramref name="status"/> donde el sujeto validado es el RL de un comprador PJ.</summary>
    private ProcedureInstance StubTraspasoPjGraph(Guid id, Guid tenant, string status)
    {
        var i = Lean(id, tenant, "TRM-2026-013194", traspaso: true);
        i.Status = status;
        i.DraftFinalizedAt = null;
        i.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            PersonType = "juridical",
            FullName = "Empresa de Prueba SAS",
            Metadata = "{\"representanteLegal\":{\"tipoDocumento\":\"" + TipoDoc + "\",\"numeroDocumento\":\"" + Documento + "\"}}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        i.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "vendedor",
            DocumentType = TipoDoc,
            DocumentNumber = "5550001",
            FullName = "Vendedor de Prueba",
            Metadata = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, Arg.Any<CancellationToken>()).Returns(i);
        return i;
    }

    [Fact]
    public async Task Bug13194_PersonaJuridicaEnTramiteAsignado_FirmaLaParteDelRepresentante()
    {
        // La validación se hizo como "vendedor" en OTRO trámite; aquí la persona es el RL del comprador PJ.
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        AprobadaValidation(validationId, tenant, parte: "vendedor");
        var id = Guid.NewGuid();
        var graph = StubTraspasoPjGraph(id, tenant, TramiteEstado.Asignado);
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance> { graph });
        _repo.ListInstanceIdsConEventoDeValidacionAsync(tenant, "firma_auto_solicitada", validationId, ct)
            .Returns(new HashSet<Guid>());

        var result = await _sut.HandleAsync(Event(validationId, tenant, "vendedor"), ct);

        result.Processed.Should().Be(1);
        result.Skipped.Should().BeEmpty();
        graph.Signatures.Should().ContainSingle(s => s.Parte == "comprador" && s.DocTipo == SignatureDocTipos.Compraventa,
            "se firma la parte donde la persona es sujeto de identidad (RL del comprador PJ), no la validada");
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceEvent>(e =>
            e.Tipo == "firma_auto_solicitada" && e.Payload.Contains("\"estado\":\"asignado\"")));
    }

    [Fact]
    public async Task Bug13194_ReEntregaDelEvento_NoVuelveAFirmarLoYaAplicado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        AprobadaValidation(validationId, tenant);
        var id = Guid.NewGuid();
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance> { Lean(id, tenant, "TRM-2026-000013", traspaso: true) });
        _repo.ListInstanceIdsConEventoDeValidacionAsync(tenant, "firma_auto_solicitada", validationId, ct)
            .Returns(new HashSet<Guid> { id });

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Matched.Should().Be(1);
        result.Processed.Should().Be(0);
        result.Skipped.Should().ContainSingle().Which.Should().EndWith(":ya_aplicado");
        await _repo.DidNotReceive().GetByIdWithFurGraphAsync(id, tenant, Arg.Any<CancellationToken>());
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceEvent>());
    }

    [Fact]
    public async Task Bug13194_TramiteEntregado_NoSeFirma_DefensaEnProfundidad()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var validationId = Guid.NewGuid();
        AprobadaValidation(validationId, tenant);
        var id = Guid.NewGuid();
        var entregado = Lean(id, tenant, "TRM-2026-000014", traspaso: true);
        entregado.Status = TramiteEstado.Entregado;
        _repo.ListPendientesDeFirmaPorSujetoAsync(tenant, TipoDoc, Documento, ct)
            .Returns(new List<ProcedureInstance> { entregado });
        _repo.ListInstanceIdsConEventoDeValidacionAsync(tenant, "firma_auto_solicitada", validationId, ct)
            .Returns(new HashSet<Guid>());

        var result = await _sut.HandleAsync(Event(validationId, tenant), ct);

        result.Processed.Should().Be(0);
        result.Skipped.Should().ContainSingle().Which.Should().EndWith(":estado_entregado");
        await _repo.DidNotReceive().GetByIdWithFurGraphAsync(id, tenant, Arg.Any<CancellationToken>());
    }
}

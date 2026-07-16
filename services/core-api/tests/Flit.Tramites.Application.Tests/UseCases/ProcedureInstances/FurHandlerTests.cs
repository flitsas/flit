using System.Text;
using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
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

public sealed class FurHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IFurDocumentGenerator _generator = new MockFurDocumentGenerator();
    private readonly FakeCertClient _certClient = new();
    private readonly IRuesCertificateGenerator _ruesGenerator = Substitute.For<IRuesCertificateGenerator>();
    private readonly IRnmcCertificateGenerator _rnmcGenerator = Substitute.For<IRnmcCertificateGenerator>();
    private readonly IProcedureInstancePrendaRepository _prendaRepo = Substitute.For<IProcedureInstancePrendaRepository>();
    private readonly FakeStorage _storage = new();
    private readonly GenerarFurHandler _handler;

    /// <summary>Datos con los que el handler invocó al generador RNMC; null si no lo invocó.</summary>
    private RnmcCertificateData? CapturedRnmcData =>
        _rnmcGenerator.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRnmcCertificateGenerator.GenerateRnmcCertificate))
            .Select(c => (RnmcCertificateData)c.GetArguments()[0]!)
            .LastOrDefault();

    public FurHandlerTests()
    {
        _ruesGenerator.GenerateRuesCertificate(Arg.Any<RuesCertificateData>())
            .Returns(ci =>
            {
                var d = ci.Arg<RuesCertificateData>();
                return new GeneratedDocument("certificado_rues", $"certificado_rues_{d.Nit}.pdf",
                    "application/pdf", Encoding.UTF8.GetBytes($"RUES {d.RazonSocial} {d.Nit} {d.Estado}"));
            });
        _rnmcGenerator.GenerateRnmcCertificate(Arg.Any<RnmcCertificateData>())
            .Returns(ci =>
            {
                var d = ci.Arg<RnmcCertificateData>();
                return new GeneratedDocument("certificado_rnmc", $"certificado_rnmc_{d.ReferenceNumber}.pdf",
                    "application/pdf", Encoding.UTF8.GetBytes($"%PDF RNMC {d.Entradas.Count}"));
            });
        _handler = new GenerarFurHandler(_repo, _generator, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);
    }

    /// <summary>
    /// Cliente de certificado Kyverum de prueba: por defecto devuelve un PDF; configurable para simular
    /// "sin certificado" (null / 404) o un fallo (excepción transitoria).
    /// </summary>
    private sealed class FakeCertClient : IKyverumCertificateClient
    {
        public bool ReturnNull { get; set; }
        public Exception? Throw { get; set; }
        public List<string> RequestedIds { get; } = [];

        public Task<KyverumCertificate?> DownloadCertificateAsync(string verificationId, CancellationToken ct = default)
        {
            RequestedIds.Add(verificationId);
            if (Throw is not null)
                throw Throw;
            if (ReturnNull)
                return Task.FromResult<KyverumCertificate?>(null);
            return Task.FromResult<KyverumCertificate?>(
                new KyverumCertificate(Encoding.UTF8.GetBytes("%PDF-1.4 fake"), "application/pdf", $"certificado_{verificationId}.pdf"));
        }
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public List<string> Deleted { get; } = [];

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_{Saved.Count}";
            Saved.Add(path);
            return new StoredFile(path, $"sha-{tipo}", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Deleted.Add(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string tipologia) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = tipologia == TramiteTipologiaCatalog.CodigoTraspasoStandard ? "traspaso" : "matricula_inicial",
            TipologiaCodigo = tipologia,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Setea el organismo de tránsito (transit_office_code) para satisfacer el gate.</summary>
    private static void WithOrganismo(ProcedureInstance instance) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });

    private static ProcedureInstanceBiometricValidation Bio(string? parte) =>
        new()
        {
            Id = Guid.NewGuid(),
            PartyRole = parte,
            Status = BiometricEstados.Aprobado,
            // Provider kyverum + id ⇒ GenerarFurHandler descarga el certificado real de esa parte.
            Provider = BiometricProviders.Kyverum,
            KyverumVerificationId = $"kyv-{parte ?? "titular"}",
            Name = "X",
            DocumentType = "CC",
            DocumentNumber = "1",
            Email = "x@y.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Generar_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithFurGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Generar_Traspaso_WithoutBiometria_GeneratesFurNoFirmadoWithoutCertificate()
    {
        // HU #10463 AC1/AC5: sin validación (falta vendedor) el FUR se genera igual (NO biometria_gate)
        // y NO se emite el certificado de identidad.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio("comprador")); // falta vendedor -> identidad no válida
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        // FUR + compraventa (traspaso), SIN certificado_identidad.
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur", "compraventa"]);
        instance.Events.Should().ContainSingle(e => e.Tipo == "fur_generado");
    }

    [Fact]
    public async Task Generar_Matricula_WithoutBiometria_GeneratesOnlyFurNoCertificate()
    {
        // HU #10463 AC1/AC5: matrícula sin biométrica del comprador → FUR sin certificado.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance); // sin biométrica

        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur"]);
        instance.Events.Should().ContainSingle(e => e.Tipo == "fur_generado");
    }

    private static ProcedureInstanceActor ActorJuridico(ProcedureInstance instance) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "EMPRESA DEMO S.A.S.",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Generar_ConActorPersonaJuridica_GeneraCertificadoRuesSystem()
    {
        // HU #10589 AC: un actor persona jurídica (NIT) genera el certificado RUES (Source=system)
        // que se incorpora al consolidado.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorJuridico(instance));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().Contain("certificado_rues");
        instance.Attachments.Should().Contain(a => a.Tipo == "certificado_rues" && a.Source == "system");
    }

    [Fact]
    public async Task Generar_SinActorPersonaJuridica_NoGeneraCertificadoRues()
    {
        // Sin actor NIT no se emite el certificado RUES.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_rues");
    }

    [Fact]
    public async Task Generar_Traspaso_BothAprobadas_GeneratesFurAndCompraventa()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio("comprador"));
        instance.BiometricValidations.Add(Bio("vendedor"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        // FUR + certificado de identidad del comprador + del vendedor + compraventa (traspaso).
        result!.Documents.Should().HaveCount(4);
        result.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(
            ["fur", "certificado_identidad", "certificado_identidad_vendedor", "compraventa"]);
        instance.Attachments.Should().HaveCount(4);
        // Se descarga el certificado de Kyverum de CADA parte (por su verificationId propio).
        _certClient.RequestedIds.Should().BeEquivalentTo(["kyv-comprador", "kyv-vendedor"]);
        instance.Events.Should().ContainSingle(e => e.Tipo == "fur_generado");
        _repo.Received(4).Add(Arg.Any<ProcedureInstanceAttachment>());
        _repo.Received(1).Add(Arg.Any<ProcedureInstanceEvent>());
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Generar_Matricula_CompradorAprobada_GeneratesOnlyFur()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio(parte: "comprador")); // matrícula = comprador
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        // Matrícula: FUR + certificado de identidad (sin compraventa).
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur", "certificado_identidad"]);
        _certClient.RequestedIds.Should().ContainSingle().Which.Should().Be("kyv-comprador");
    }

    [Fact]
    public async Task Generar_CertificadoDownloadFails_GeneratesFurWithoutCertificate()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio(parte: "comprador"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        // Kyverum caído: la descarga del certificado lanza. NO debe bloquear ni abortar el FUR.
        _certClient.Throw = new KyverumCertificateException("Kyverum no disponible.", transient: true);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        // Sin mock: solo el FUR; el certificado se omite tras el warning.
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur"]);
        instance.Attachments.Should().NotContain(a => a.Tipo == "certificado_identidad");
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Generar_CertificadoNotAvailable_GeneratesFurWithoutCertificate()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio(parte: "comprador"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        // Kyverum sin certificado para ese id (404 → null).
        _certClient.ReturnNull = true;

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur"]);
    }

    [Fact]
    public async Task Generar_MockProvider_SkipsCertificateDownload()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        // Validación mock (sin id Kyverum): no hay certificado externo que descargar.
        var bio = Bio(parte: "comprador");
        bio.Provider = BiometricProviders.Mock;
        bio.KyverumVerificationId = null;
        instance.BiometricValidations.Add(bio);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur"]);
        _certClient.RequestedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Generar_Matricula_WithoutOrganismo_RejectsGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        instance.BiometricValidations.Add(Bio(parte: "comprador")); // biométrica ok, falta organismo
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().Be("organismo_requerido");
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Generar_Idempotent_ReplacesPreviousFur()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio(parte: "comprador"));
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Tipo = "fur",
            Filename = "old.txt",
            Mimetype = "text/plain",
            StoragePath = "old/fur",
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        _storage.Deleted.Should().Contain("old/fur");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "fur");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "certificado_identidad");
    }

    // ── HU #10762 · Certificado RNMC ──────────────────────────────────────

    private static ProcedureInstanceActor ActorNatural(ProcedureInstance instance, string rol, string nombre, string doc) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = rol,
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = nombre,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>
    /// Snapshot de preflight con los checks indicados. FEATURE 05 — el certificado RNMC ya no lee del
    /// snapshot sino del field_value <c>rnmc_checks</c>: este helper además lo siembra con los mismos
    /// checks (con <c>createdAt</c> como fecha de consulta), para que el certificado se genere igual.
    /// </summary>
    private static ProcedureInstancePreflightSnapshot Snapshot(
        ProcedureInstance instance, DateTimeOffset createdAt, params PreflightCheckDto[] checks)
    {
        var json = JsonSerializer.Serialize(checks);
        var existing = instance.FieldValues.FirstOrDefault(f => f.FieldKey == "rnmc_checks");
        if (existing is not null)
        {
            existing.ValueJson = json;
        }
        else
        {
            instance.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = instance.TenantId,
                ProcedureInstanceId = instance.Id,
                FieldKey = "rnmc_checks",
                ValueJson = json,
                Source = "system",
                CreatedAt = createdAt,
            });
        }

        return new ProcedureInstancePreflightSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Overall = "green",
            Checks = json,
            Provider = "verifik_rnmc",
            CreatedAt = createdAt,
        };
    }

    [Fact]
    public async Task Generar_SinSnapshotPreflight_NoGeneraCertificadoRnmc()
    {
        // AC1: sin preflight corrido no hay dato RNMC que certificar.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns((ProcedureInstancePreflightSnapshot?)null);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_rnmc");
        CapturedRnmcData.Should().BeNull();
    }

    [Fact]
    public async Task Generar_SnapshotSinChecksRnmc_NoGeneraCertificadoRnmc()
    {
        // AC1: preflight corrido pero sin consulta RNMC (p. ej. persona jurídica) → no se emite.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("runt_vehiculo", "RUNT", "ok", "verifik_runt", null)));

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_rnmc");
        CapturedRnmcData.Should().BeNull();
    }

    [Fact]
    public async Task Generar_ConChecksRnmc_GeneraCertificadoRnmcSystem()
    {
        // AC1/AC2: con checks rnmc_ se emite UN certificado_rnmc (Source=system) adjunto al trámite.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                "ok", "verifik_rnmc", null)));

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().ContainSingle(t => t == "certificado_rnmc");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "certificado_rnmc" && a.Source == "system");

        // Los datos del actor se toman de instance.Actors, no del check.
        var data = CapturedRnmcData!;
        data.Entradas.Should().ContainSingle();
        data.Entradas[0].Rol.Should().Be("comprador");
        data.Entradas[0].Nombre.Should().Be("DANIEL AMADO");
        data.Entradas[0].Documento.Should().Be("1193552679");
    }

    [Fact]
    public async Task Generar_ConChecksRnmc_MapeaEstadosPorParte()
    {
        // AC2: ok → SIN MEDIDAS CORRECTIVAS; warn → CON MEDIDAS CORRECTIVAS. El rol sale del segmento
        // intermedio de la key (rnmc_{rol}_{key}) y el detalle del Message del check.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        instance.Actors.Add(ActorNatural(instance, "vendedor", "MARIA LOPEZ", "52123456"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                "ok", "verifik_rnmc", null),
            new PreflightCheckDto("rnmc_vendedor_medidas_correctivas", "Medidas correctivas (Policía)",
                "warn", "verifik_rnmc", "1 medida(s): Riña")));

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        var data = CapturedRnmcData!;
        data.Entradas.Should().HaveCount(2);

        var comprador = data.Entradas.Single(e => e.Rol == "comprador");
        comprador.Estado.Should().Be("SIN MEDIDAS CORRECTIVAS");
        comprador.Detalle.Should().BeNull();

        var vendedor = data.Entradas.Single(e => e.Rol == "vendedor");
        vendedor.Estado.Should().Be("CON MEDIDAS CORRECTIVAS");
        vendedor.Nombre.Should().Be("MARIA LOPEZ");
        vendedor.Detalle.Should().Be("1 medida(s): Riña");
    }

    [Theory]
    [InlineData("unknown", "SIN DATOS")]
    [InlineData("error", "NO VERIFICABLE")]
    public async Task Generar_ConCheckRnmcSinResultado_MapeaEstadoNoConcluyente(string status, string esperado)
    {
        // AC2: unknown → SIN DATOS; error → NO VERIFICABLE. Nunca se afirma "sin medidas" sin dato.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                status, "verifik_rnmc", null)));

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        CapturedRnmcData!.Entradas.Single().Estado.Should().Be(esperado);
    }

    [Fact]
    public async Task Generar_ConChecksRnmc_ConsultadoEnEsLaFechaDelSnapshot()
    {
        // AC2: la fecha de consulta del certificado es la de la corrida de preflight, NO la de generación
        // del FUR: certificar "consultado hoy" con datos de ayer sería falso.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        var consultadoEn = new DateTimeOffset(2026, 7, 10, 8, 15, 0, TimeSpan.Zero);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, consultadoEn,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                "ok", "verifik_rnmc", null)));

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        CapturedRnmcData!.ConsultadoEn.Should().Be(consultadoEn);
    }

    [Fact]
    public async Task Generar_ConChecksRnmc_NoFiltraElProveedorAlCertificado()
    {
        // AC2 (regla dura): el nombre del integrador (verifik) NUNCA llega al documento del usuario; la
        // fuente que se muestra es la entidad oficial y la pinta el generador como literal.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                "ok", "verifik_rnmc", null)));

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        var serialized = JsonSerializer.Serialize(CapturedRnmcData!);
        serialized.Should().NotContainEquivalentOf("verifik");
    }

    [Fact]
    public async Task Generar_SinChecksRnmc_BorraElCertificadoRnmcPrevio()
    {
        // AC2: regeneración sin RNMC (p. ej. el actor pasó a persona jurídica) no puede dejar el
        // certificado obsoleto en el expediente.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Tipo = "certificado_rnmc",
            Filename = "certificado_rnmc_viejo.pdf",
            Mimetype = "application/pdf",
            StoragePath = "old/certificado_rnmc",
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns((ProcedureInstancePreflightSnapshot?)null);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        _storage.Deleted.Should().Contain("old/certificado_rnmc");
        instance.Attachments.Should().NotContain(a => a.Tipo == "certificado_rnmc");
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_rnmc");
        _repo.Received(1).RemoveAttachment(Arg.Is<ProcedureInstanceAttachment>(a => a.Tipo == "certificado_rnmc"));
    }

    [Fact]
    public async Task Generar_GeneradorRnmcFalla_NoBloqueaElFur_YConservaElCertificadoPrevio()
    {
        // AC3: el certificado RNMC es best-effort — si el generador falla, el FUR se emite igual.
        // Y el certificado previo se CONSERVA: el RNMC sí aplicaba, así que el previo sigue siendo válido;
        // borrarlo por un fallo transitorio perdería un documento bueno (a diferencia de "ya no aplica").
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Tipo = "certificado_rnmc",
            Filename = "certificado_rnmc_previo.pdf",
            Mimetype = "application/pdf",
            StoragePath = "old/certificado_rnmc",
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                "ok", "verifik_rnmc", null)));
        _rnmcGenerator.GenerateRnmcCertificate(Arg.Any<RnmcCertificateData>())
            .Returns(_ => throw new InvalidOperationException("QuestPDF caído"));

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().Contain("fur").And.NotContain("certificado_rnmc");
        _storage.Deleted.Should().NotContain("old/certificado_rnmc");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "certificado_rnmc");
        await _repo.Received(1).SaveChangesAsync(ct);
    }
}

public sealed class MockFurDocumentGeneratorTests
{
    private static FurDocumentData Data() =>
        new(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "TRM-2026-000001",
            Modalidad: "traspaso",
            TipologiaCodigo: "traspaso_standard",
            Vehiculo: new VehiculoDatos(
                Marca: "TOYOTA", Linea: "COROLLA", Modelo: "2024", Color: "ROJO",
                Clase: "AUTOMOVIL", Combustible: "GASOLINA", Cilindraje: "1800",
                Vin: "1HGCM82633A004352", Placa: "ABC123"),
            Organismo: new OrganismoTransito(Codigo: "11001000", Nombre: "SDM Bogotá", Ciudad: "Bogotá"),
            Partes: [new DocumentParte("comprador", "Juan", "123", "j@x.com")],
            ValorVenta: 50000m,
            Causal: "venta",
            SellosFirma: ["comprador/compraventa: abc (2026)"]);

    [Fact]
    public void GenerateFur_EmbedsRealData()
    {
        var doc = new MockFurDocumentGenerator().GenerateFur(Data());

        doc.Tipo.Should().Be("fur");
        doc.Mimetype.Should().Be("text/plain");
        var content = Encoding.UTF8.GetString(doc.Content);
        content.Should().Contain("TRM-2026-000001");
        content.Should().Contain("1HGCM82633A004352");
        content.Should().Contain("TOYOTA");
        content.Should().Contain("COROLLA");
        content.Should().Contain("SDM Bogotá");
        content.Should().Contain("11001000");
        content.Should().Contain("Juan");
        content.Should().Contain("MOCK FUR");
    }

    [Fact]
    public void GenerateCompraventa_ContainsValor()
    {
        var doc = new MockFurDocumentGenerator().GenerateCompraventa(Data());

        doc.Tipo.Should().Be("compraventa");
        Encoding.UTF8.GetString(doc.Content).Should().Contain("50000.00");
    }
}

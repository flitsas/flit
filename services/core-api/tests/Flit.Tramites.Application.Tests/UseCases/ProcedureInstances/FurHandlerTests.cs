using System.Text;
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
    private readonly IProcedureInstancePrendaRepository _prendaRepo = Substitute.For<IProcedureInstancePrendaRepository>();
    private readonly FakeStorage _storage = new();
    private readonly GenerarFurHandler _handler;

    public FurHandlerTests()
    {
        _ruesGenerator.GenerateRuesCertificate(Arg.Any<RuesCertificateData>())
            .Returns(ci =>
            {
                var d = ci.Arg<RuesCertificateData>();
                return new GeneratedDocument("certificado_rues", $"certificado_rues_{d.Nit}.pdf",
                    "application/pdf", Encoding.UTF8.GetBytes($"RUES {d.RazonSocial} {d.Nit} {d.Estado}"));
            });
        _handler = new GenerarFurHandler(_repo, _generator, _certClient, _ruesGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);
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

    /// <summary>Generador FUR que captura el <see cref="FurDocumentData"/> ensamblado para aserciones.</summary>
    private sealed class CapturingFurGenerator : IFurDocumentGenerator
    {
        public FurDocumentData? Captured { get; private set; }

        public GeneratedDocument GenerateFur(FurDocumentData data)
        {
            Captured = data;
            return new GeneratedDocument("fur", "fur.pdf", "application/pdf", [1, 2, 3]);
        }

        public GeneratedDocument GenerateCompraventa(FurDocumentData data)
        {
            Captured = data;
            return new GeneratedDocument("compraventa", "cv.pdf", "application/pdf", [1, 2, 3]);
        }
    }

    private static void AddFieldValue(ProcedureInstance instance, string key, string value) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = key,
            ValueText = value,
            Source = "user",
        });

    [Fact]
    public async Task Generar_ConTransformacion_FurUsaRuntEnCamposYNuevoEnObservaciones()
    {
        // A4/B4 (HU #10673, ADR-0029): el FUR imprime el color/combustible ORIGINAL del RUNT en los CAMPOS
        // del vehículo; la transformación declarada (valor nuevo) va SOLO en observaciones y sin flecha.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        AddFieldValue(instance, "vehicle_color_runt", "PLATA");
        AddFieldValue(instance, "vehicle_color", "NEGRO");
        AddFieldValue(instance, "cambio_color", "true");
        AddFieldValue(instance, "vehicle_fuel_runt", "GASOLINA");
        AddFieldValue(instance, "vehicle_fuel", "DIESEL");
        AddFieldValue(instance, "cambio_combustible", "true");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured.Should().NotBeNull();
        capturing.Captured!.Vehiculo.Color.Should().Be("PLATA");         // campo del FUR = RUNT original
        capturing.Captured.Vehiculo.Combustible.Should().Be("GASOLINA"); // campo del FUR = RUNT original
        capturing.Captured.Observaciones.Should().Be("Cambio de color: NEGRO. Cambio de combustible: DIESEL.");
    }

    [Fact]
    public async Task Generar_SinSnapshotRunt_FurCaeAlEfectivo()
    {
        // Trámite previo a la feature (sin *_runt): los campos del FUR caen al valor efectivo.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        AddFieldValue(instance, "vehicle_color", "AZUL");
        AddFieldValue(instance, "vehicle_fuel", "GASOLINA");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured!.Vehiculo.Color.Should().Be("AZUL");
        capturing.Captured.Vehiculo.Combustible.Should().Be("GASOLINA");
        capturing.Captured.Observaciones.Should().BeNull(); // sin cambio declarado, sin texto automático
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

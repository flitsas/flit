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

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class FurHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IFurDocumentGenerator _generator = new MockFurDocumentGenerator();
    private readonly FakeCertClient _certClient = new();
    private readonly FakeStorage _storage = new();
    private readonly GenerarFurHandler _handler;

    public FurHandlerTests()
    {
        _handler = new GenerarFurHandler(_repo, _generator, _certClient, _storage, NullLogger<GenerarFurHandler>.Instance);
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
            Status = ProcedureInstanceStatus.Draft,
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
            // Provider kyverum + id ⇒ GenerarFurHandler descarga el certificado real del comprador.
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
        // FUR + certificado de identidad + compraventa (traspaso).
        result!.Documents.Should().HaveCount(3);
        result.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur", "certificado_identidad", "compraventa"]);
        instance.Attachments.Should().HaveCount(3);
        instance.Events.Should().ContainSingle(e => e.Tipo == "fur_generado");
        _repo.Received(3).Add(Arg.Any<ProcedureInstanceAttachment>());
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

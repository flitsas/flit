using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class FurHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IFurDocumentGenerator _generator = new MockFurDocumentGenerator();
    private readonly FakeStorage _storage = new();
    private readonly GenerarFurHandler _handler;

    public FurHandlerTests()
    {
        _handler = new GenerarFurHandler(_repo, _generator, _storage);
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

        public void Delete(string storagePath) => Deleted.Add(storagePath);
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

    private static ProcedureInstanceBiometricValidation Bio(string? parte) =>
        new()
        {
            Id = Guid.NewGuid(), Parte = parte, Estado = BiometricEstados.Aprobado,
            Nombre = "X", TipoDoc = "CC", Documento = "1", Email = "x@y.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), CreatedAt = DateTimeOffset.UtcNow,
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
    public async Task Generar_Traspaso_WithoutBiometria_RejectsGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.BiometricValidations.Add(Bio("comprador")); // falta vendedor
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().Be("biometria_gate");
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Generar_Traspaso_BothAprobadas_GeneratesFurAndCompraventa()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.BiometricValidations.Add(Bio("comprador"));
        instance.BiometricValidations.Add(Bio("vendedor"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Should().HaveCount(2);
        result.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur", "compraventa"]);
        instance.Attachments.Should().HaveCount(2);
        instance.Events.Should().ContainSingle(e => e.Tipo == "fur_generado");
        _repo.Received(2).Add(Arg.Any<ProcedureInstanceAttachment>());
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
        instance.BiometricValidations.Add(Bio(parte: "comprador")); // matrícula = comprador
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Should().ContainSingle().Which.Tipo.Should().Be("fur");
    }

    [Fact]
    public async Task Generar_Idempotent_ReplacesPreviousFur()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        instance.BiometricValidations.Add(Bio(parte: "comprador"));
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(), TenantId = tenant, ProcedureInstanceId = id,
            Tipo = "fur", Filename = "old.txt", Mimetype = "text/plain",
            StoragePath = "old/fur", Source = "system", UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Should().ContainSingle();
        _storage.Deleted.Should().Contain("old/fur");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "fur");
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
            Vin: "1HGCM82633A004352",
            Placa: "ABC123",
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

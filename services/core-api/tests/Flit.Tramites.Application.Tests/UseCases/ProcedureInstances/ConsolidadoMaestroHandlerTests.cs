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

public sealed class ConsolidadoMaestroHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeMerger _merger = new();
    private readonly FakeStorage _storage = new();
    private readonly GenerarConsolidadoMaestroHandler _handler;

    public ConsolidadoMaestroHandlerTests()
    {
        _handler = new GenerarConsolidadoMaestroHandler(_repo, _merger, _storage);
    }

    private sealed class FakeMerger : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;
        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = new();

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            var path = $"{procedureInstanceId:D}/{tipo}_{Saved.Count}";
            Files[path] = bytes;
            Saved.Add(path);
            return new StoredFile(path, $"sha-{tipo}", bytes.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Files.Remove(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
        {
            if (!Files.TryGetValue(storagePath, out var bytes))
                return Task.FromResult<Stream?>(null);
            return Task.FromResult<Stream?>(new MemoryStream(bytes));
        }

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private ProcedureInstance InstanceWithAttachments(Guid id, Guid tenantId, params (string tipo, string filename)[] attachments)
    {
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000099",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var (tipo, filename) in attachments)
        {
            var path = $"{id:D}/{tipo}";
            var content = System.Text.Encoding.UTF8.GetBytes($"%PDF-{filename}");
            _storage.Files[path] = content;
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                Tipo = tipo,
                Filename = filename,
                Mimetype = "application/pdf",
                SizeBytes = content.Length,
                Sha256 = $"sha-{tipo}",
                StoragePath = path,
                Source = "user",
                UploadedAt = DateTimeOffset.UtcNow,
            });
        }
        return instance;
    }

    [Fact]
    public async Task HandleAsync_InstanceNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct: ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_SinAdjuntosFuenteValidos_ReturnsSinAdjuntos()
    {
        // Solo tiene consolidados (que deben ser excluidos) → sin adjuntos para el maestro.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceWithAttachments(id, tenantId,
            ("consolidado", "consolidado.pdf"),
            ("consolidado_maestro", "maestro.pdf"));
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, ct: ct);

        error.Should().Be("sin_adjuntos");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ExcluyeConsolidadosPreviosAlFusionar()
    {
        // Los adjuntos de tipo consolidado y consolidado_maestro no deben aparecer en el resultado.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceWithAttachments(id, tenantId,
            ("factura", "factura.pdf"),
            ("fur", "fur.pdf"),
            ("consolidado", "consolidado_prev.pdf"),
            ("consolidado_maestro", "maestro_prev.pdf"));
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, ct: ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Document.Tipo.Should().Be("consolidado_maestro");

        // El evento debe listar solo los adjuntos fuente, sin los consolidados excluidos.
        var evento = instance.Events.Should().ContainSingle(e => e.Tipo == "consolidado_maestro_generado").Which;
        evento.Payload.Should().NotContain("consolidado_prev");
        evento.Payload.Should().NotContain("maestro_prev");
    }

    [Fact]
    public async Task HandleAsync_ConAdjuntosValidos_PersisteTipoConsolidadoMaestro()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceWithAttachments(id, tenantId,
            ("factura", "factura.pdf"),
            ("aduana", "aduana.pdf"),
            ("fur", "fur.pdf"));
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, ct: ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Document.Tipo.Should().Be("consolidado_maestro");
        result.Document.Filename.Should().StartWith("consolidado_maestro_");
        _storage.Saved.Should().ContainSingle();
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EsIdempotente_RemplazaMaestroAnterior()
    {
        // Un segundo llamado debe eliminar el maestro previo y crear uno nuevo.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceWithAttachments(id, tenantId,
            ("factura", "factura.pdf"),
            ("fur", "fur.pdf"));
        // Simular un consolidado_maestro previo ya persistido.
        var prevPath = $"{id:D}/consolidado_maestro_prev";
        _storage.Files[prevPath] = System.Text.Encoding.UTF8.GetBytes("%PDF-prev");
        var prevAttachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = "consolidado_maestro",
            Filename = "prev.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 8,
            Sha256 = "sha-prev",
            StoragePath = prevPath,
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        instance.Attachments.Add(prevAttachment);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, ct: ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        // El archivo previo fue borrado del storage.
        _storage.Files.Should().NotContainKey(prevPath);
        // Solo se guardó un nuevo archivo.
        _storage.Saved.Should().ContainSingle();
        _repo.Received(1).RemoveAttachment(prevAttachment);
    }

    [Fact]
    public async Task HandleAsync_SinFur_ProcedeSinError()
    {
        // A diferencia del wizard, el maestro NO requiere FUR → genera con lo disponible.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceWithAttachments(id, tenantId,
            ("factura", "factura.pdf"),
            ("impronta", "impronta.pdf"));
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, ct: ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Document.Tipo.Should().Be("consolidado_maestro");
    }

    [Fact]
    public async Task HandleAsync_ConMatrizPrecedencia_OrdenaSegunLaMatriz()
    {
        // HU #10706 AC1 — con una precedencia resuelta de matriz, el orden la sigue (tras los
        // documentos generados de cabecera) y los no clasificados van al final como "Anexos".
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = InstanceWithAttachments(id, tenantId,
            ("fur", "fur.pdf"),
            ("factura", "factura.pdf"),
            ("aduana", "aduana.pdf"),
            ("soat", "soat.pdf"));
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        // Matriz OT: SOAT antes que factura. "aduana" no está en la matriz → va al final (Anexos).
        var precedencia = new[] { "soat", "factura" };

        var (result, error) = await _handler.HandleAsync(id, tenantId, precedencia, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();

        var payload = instance.Events
            .Should().ContainSingle(e => e.Tipo == "consolidado_maestro_generado").Which.Payload;
        // Orden esperado en paginas_incluidas: fur (generado, cabecera) → soat → factura → aduana (anexo).
        payload.IndexOf("fur", StringComparison.Ordinal)
            .Should().BeLessThan(payload.IndexOf("soat", StringComparison.Ordinal));
        payload.IndexOf("soat", StringComparison.Ordinal)
            .Should().BeLessThan(payload.IndexOf("factura", StringComparison.Ordinal));
        payload.IndexOf("factura", StringComparison.Ordinal)
            .Should().BeLessThan(payload.IndexOf("aduana", StringComparison.Ordinal));
    }
}

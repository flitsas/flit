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

public sealed class ConsolidadoHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeMerger _merger = new();
    private readonly FakeStorage _storage = new();
    private readonly GenerarConsolidadoHandler _handler;

    public ConsolidadoHandlerTests()
    {
        _handler = new GenerarConsolidadoHandler(_repo, _merger, _storage);
    }

    private sealed class FakeMerger : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) =>
            pdfParts.SelectMany(x => x).ToArray();
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
    }

    private static ProcedureInstance MatriculaInstance(Guid id, Guid tenantId)
    {
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000099",
            Status = ProcedureInstanceStatus.Draft,
            ModalidadEntrada = "matricula_inicial",
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoMatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        AddAttachment(instance, "factura", "factura.pdf", "%PDF-factura");
        AddAttachment(instance, "aduana", "aduana.pdf", "%PDF-aduana");
        AddAttachment(instance, "impronta", "impronta.pdf", "%PDF-impronta");
        AddAttachment(instance, "fur", "fur.pdf", "%PDF-fur");
        AddAttachment(instance, "certificado_identidad", "cert.pdf", "%PDF-cert");

        return instance;
    }

    private static ProcedureInstance TraspasoInstance(Guid id, Guid tenantId)
    {
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000100",
            Status = ProcedureInstanceStatus.Draft,
            ModalidadEntrada = "traspaso",
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoTraspasoStandard,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Obligatorios del checklist de traspaso + FUR + certificado + compraventa.
        AddAttachment(instance, "fur", "fur.pdf", "%PDF-fur");
        AddAttachment(instance, "certificado_identidad", "cert.pdf", "%PDF-cert");
        AddAttachment(instance, "compraventa", "compraventa.pdf", "%PDF-compraventa");
        AddAttachment(instance, "impronta", "impronta.pdf", "%PDF-impronta");
        AddAttachment(instance, "soat", "soat.pdf", "%PDF-soat");
        AddAttachment(instance, "rtm", "rtm.pdf", "%PDF-rtm");
        AddAttachment(instance, "paz_salvo", "paz_salvo.pdf", "%PDF-pazsalvo");
        AddAttachment(instance, "cedulas", "cedulas.pdf", "%PDF-cedulas");

        return instance;
    }

    private static void AddAttachment(ProcedureInstance instance, string tipo, string filename, string contentMarker)
    {
        var id = Guid.NewGuid();
        var path = $"{instance.Id:D}/{tipo}";
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = id,
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = filename,
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = $"sha-{tipo}",
            StoragePath = path,
            Source = tipo is "fur" or "certificado_identidad" ? "system" : "user",
            UploadedAt = DateTimeOffset.UtcNow,
        });
    }

    [Fact]
    public async Task HandleAsync_ModalidadDesconocida_ReturnsModalidadNoSoportada()
    {
        // AC3 (#10455): una modalidad no habilitada distinta de matrícula/traspaso sigue rechazando.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.ModalidadEntrada = "sucesion";

        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        result.Should().BeNull();
        error.Should().Be("modalidad_no_soportada");
    }

    [Fact]
    public async Task HandleAsync_TraspasoSinFur_ReturnsFurRequerido()
    {
        // AC2 (#10455): traspaso sin FUR → fur_requerido, NUNCA modalidad_no_soportada.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "fur"));

        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        result.Should().BeNull();
        error.Should().Be(SubmitGate.FurRequerido);
    }

    [Fact]
    public async Task HandleAsync_TraspasoCompleto_IncluyeCompraventa()
    {
        // AC1 (#10455): traspaso completo → consolidado con la compraventa en paginas_incluidas.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);

        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);

        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Document.Tipo.Should().Be("consolidado");
        instance.Events.Should().ContainSingle(e => e.Tipo == "consolidado_generado")
            .Which.Payload.Should().Contain("compraventa");
    }

    [Fact]
    public async Task HandleAsync_SinFur_ReturnsFurRequerido()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "fur"));

        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        result.Should().BeNull();
        error.Should().Be(SubmitGate.FurRequerido);
    }

    [Fact]
    public async Task HandleAsync_MatriculaCompleta_PersisteConsolidado()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);

        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);

        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Document.Tipo.Should().Be("consolidado");
        _storage.Saved.Should().ContainSingle();
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

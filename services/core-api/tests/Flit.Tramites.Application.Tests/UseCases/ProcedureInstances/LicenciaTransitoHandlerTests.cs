using System.Text;
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
/// Licencia de Tránsito (LT) adjuntada por el OT + descarga del consolidado + orden del
/// historial en el detalle (trazabilidad cronológica del Expediente).
/// </summary>
public sealed class LicenciaTransitoHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeStorage _storage = new();
    private readonly AdjuntarLicenciaTransitoHandler _adjuntar;
    private readonly DescargarConsolidadoHandler _descargar;

    public LicenciaTransitoHandlerTests()
    {
        _adjuntar = new AdjuntarLicenciaTransitoHandler(_repo, _storage);
        _descargar = new DescargarConsolidadoHandler(_repo, _storage);
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public List<string> Deleted { get; } = [];
        public Dictionary<string, byte[]> Contents { get; } = [];

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_{originalFilename}";
            Saved.Add(path);
            Contents[path] = ms.ToArray();
            return new StoredFile(path, "deadbeef", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Deleted.Add(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(
                Contents.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000300",
            Status = status,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static UploadAttachmentInput LtPdf(string name = "lt.pdf") =>
        new(AdjuntarLicenciaTransitoHandler.Tipo, name, "application/pdf", 512,
            new MemoryStream(Encoding.UTF8.GetBytes("licencia-transito")));

    // ── Adjuntar LT ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Aprobado)]
    public async Task AdjuntarLt_EnEstadosValidos_CreaAdjuntoYEvento(string status)
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instance(id, tenantId, status);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(instance);

        var (result, error) = await _adjuntar.HandleAsync(id, tenantId, LtPdf(), null, ct);

        error.Should().BeNull();
        result!.Tipo.Should().Be("licencia_transito");
        result.Source.Should().Be("ot");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "licencia_transito");
        instance.Events.Should().ContainSingle(e => e.Tipo == "lt_adjuntada");
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Preparado)]
    [InlineData(TramiteEstado.Rechazado)]
    [InlineData(TramiteEstado.Anulado)]
    public async Task AdjuntarLt_EnEstadoNoPermitido_Rechaza(string status)
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(Instance(id, tenantId, status));

        var (result, error) = await _adjuntar.HandleAsync(id, tenantId, LtPdf(), null, ct);

        error.Should().Be("estado_invalido");
        result.Should().BeNull();
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task AdjuntarLt_ReemplazaLaLtPrevia()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instance(id, tenantId, TramiteEstado.Aprobado);
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = "licencia_transito",
            Filename = "lt_vieja.pdf",
            Mimetype = "application/pdf",
            StoragePath = "old/lt",
            Source = "ot",
            UploadedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(instance);

        var (result, error) = await _adjuntar.HandleAsync(id, tenantId, LtPdf("lt_nueva.pdf"), null, ct);

        error.Should().BeNull();
        _storage.Deleted.Should().Contain("old/lt");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "licencia_transito");
        instance.Attachments.Single(a => a.Tipo == "licencia_transito").Filename.Should().Be("lt_nueva.pdf");
        result!.Filename.Should().Be("lt_nueva.pdf");
    }

    [Fact]
    public async Task AdjuntarLt_MimeInvalido_Rechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        var input = new UploadAttachmentInput(
            AdjuntarLicenciaTransitoHandler.Tipo, "lt.exe", "application/octet-stream", 10,
            new MemoryStream([1, 2]));

        var (result, error) = await _adjuntar.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), input, null, ct);

        error.Should().Be("invalid_mime");
        result.Should().BeNull();
    }

    // C8 (ADR-0026): la Licencia de Tránsito también acepta SOLO PDF; una imagen se rechaza.
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public async Task AdjuntarLt_Imagen_Rechaza(string imageMime)
    {
        var ct = TestContext.Current.CancellationToken;
        var input = new UploadAttachmentInput(
            AdjuntarLicenciaTransitoHandler.Tipo, "lt.jpg", imageMime, 10, new MemoryStream([1, 2]));

        var (result, error) = await _adjuntar.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), input, null, ct);

        error.Should().Be("invalid_mime");
        result.Should().BeNull();
    }

    [Fact]
    public async Task AdjuntarLt_UsuarioInexistente_RegistraUploadedByNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId, user) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var instance = Instance(id, tenantId, TramiteEstado.Entregado);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(instance);
        _repo.UserExistsAsync(user, ct).Returns(false);

        var (_, error) = await _adjuntar.HandleAsync(id, tenantId, LtPdf(), user, ct);

        error.Should().BeNull();
        instance.Attachments.Single(a => a.Tipo == "licencia_transito").UploadedBy.Should().BeNull();
    }

    // ── Descargar consolidado ─────────────────────────────────────────────────

    [Fact]
    public async Task DescargarConsolidado_SinConsolidado_DevuelveError()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct)
            .Returns(Instance(id, tenantId, TramiteEstado.Entregado));

        var (result, error) = await _descargar.HandleAsync(id, tenantId, ct);

        error.Should().Be("consolidado_no_generado");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DescargarConsolidado_DevuelveElMasReciente()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instance(id, tenantId, TramiteEstado.Aprobado);
        _storage.Contents["path/nuevo"] = Encoding.UTF8.GetBytes("pdf-nuevo");
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            Tipo = "consolidado",
            Filename = "consolidado_v1.pdf",
            Mimetype = "application/pdf",
            StoragePath = "path/viejo",
            UploadedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            Tipo = "consolidado",
            Filename = "consolidado_v2.pdf",
            Mimetype = "application/pdf",
            StoragePath = "path/nuevo",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(instance);

        var (result, error) = await _descargar.HandleAsync(id, tenantId, ct);

        error.Should().BeNull();
        result!.Filename.Should().Be("consolidado_v2.pdf");
        result.Mimetype.Should().Be("application/pdf");
    }

    // ── Trazabilidad: historial ordenado + LT en el consolidado ───────────────

    [Fact]
    public async Task DetalleInstancia_HistorialOrdenadoCronologicamente()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instance(id, tenantId, TramiteEstado.Aprobado);
        // Se agregan DESORDENADOS a propósito (EF no garantiza el orden de la colección).
        instance.StatusHistory.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            FromStatus = "entregado",
            ToStatus = "aprobado",
            ChangedAt = now,
        });
        instance.StatusHistory.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            FromStatus = null,
            ToStatus = "borrador",
            ChangedAt = now.AddHours(-3),
        });
        instance.StatusHistory.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            FromStatus = "preparado",
            ToStatus = "entregado",
            ChangedAt = now.AddHours(-1),
        });
        instance.StatusHistory.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            FromStatus = "borrador",
            ToStatus = "preparado",
            ChangedAt = now.AddHours(-2),
        });
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);

        var (detail, error) = await new GetProcedureInstanceHandler(_repo).HandleAsync(id, tenantId, ct);

        error.Should().BeNull();
        detail!.StatusHistory.Select(h => h.ToStatus)
            .Should().ContainInOrder("borrador", "preparado", "entregado", "aprobado");
    }

    [Fact]
    public async Task Consolidado_IncluyeLicenciaTransito_DespuesDelFur()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instance(id, tenantId, TramiteEstado.Aprobado);
        instance.TipologiaCodigo = Flit.Tramites.Domain.Tramites.Catalog.TramiteTipologiaCatalog.CodigoMatriculaInicial;
        await AddPdf(instance, "factura");
        await AddPdf(instance, "aduana");
        await AddPdf(instance, "impronta");
        await AddPdf(instance, "fur");
        await AddPdf(instance, "licencia_transito");
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(instance);

        var merger = new PassthroughMerger();
        var handler = new GenerarConsolidadoHandler(_repo, merger, _storage);
        var (result, error) = await handler.HandleAsync(id, tenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        // El evento consolidado_generado publica el orden real de páginas incluidas:
        // la LT entra justo después del FUR y nunca el propio consolidado.
        var evento = instance.Events.Single(e => e.Tipo == "consolidado_generado");
        var payload = System.Text.Json.JsonDocument.Parse(evento.Payload);
        var incluidas = payload.RootElement.GetProperty("paginas_incluidas")
            .EnumerateArray().Select(x => x.GetString()).ToList();
        incluidas.Should().ContainInOrder("fur", "licencia_transito", "factura");
        incluidas.Should().NotContain("consolidado");
    }

    private sealed class PassthroughMerger : Flit.Tramites.Application.Documents.IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();
    }

    private async Task AddPdf(ProcedureInstance instance, string tipo)
    {
        var stored = await _storage.SaveAsync(
            instance.Id, tipo, $"{tipo}.pdf", new MemoryStream(Encoding.UTF8.GetBytes($"%PDF-{tipo}")));
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            Mimetype = "application/pdf",
            StoragePath = stored.StoragePath,
            UploadedAt = DateTimeOffset.UtcNow,
        });
    }
}

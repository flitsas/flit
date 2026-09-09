using System.Text;
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
/// HU #12158 — acciones avanzadas del admin sobre el consolidado: "Limpiar" (AC1, siempre fuerza la
/// regeneración) y "Cargar consolidado externo" (AC3, registra Source="user"). La protección de un
/// consolidado Source="user" contra la regeneración automática (AC2) se prueba en
/// <c>ConsolidadoHandlerTests</c> (HU12158_AC2_*), junto al resto de <see cref="GenerarConsolidadoHandler"/>.
/// </summary>
public sealed class AdminConsolidadoHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeMerger _merger = new();
    private readonly FakeStorage _storage = new();

    private sealed class FakeMerger : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

        public byte[] Compose(MergeRequest request) => Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public List<string> Deleted { get; } = [];
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
            return new StoredFile(path, $"sha-{tipo}-{Saved.Count}", bytes.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath)
        {
            Deleted.Add(storagePath);
            Files.Remove(storagePath);
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Files.TryGetValue(storagePath, out var bytes)
                ? Task.FromResult<Stream?>(new MemoryStream(bytes))
                : Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private static ProcedureInstance MatriculaInstanceConFur(Guid id, Guid tenantId, bool migrado = false, string status = TramiteEstado.Borrador)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000200",
            Status = status,
            IsMigrated = migrado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        AddAttachment(instance, "fur", "fur.pdf", "system", DateTimeOffset.UtcNow);
        return instance;
    }

    private static void AddAttachment(
        ProcedureInstance instance, string tipo, string filename, string source, DateTimeOffset uploadedAt)
    {
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = filename,
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = $"sha-{tipo}-{source}",
            StoragePath = $"{instance.Id:D}/{tipo}_{source}_{Guid.NewGuid():N}",
            Source = source,
            UploadedAt = uploadedAt,
        });
    }

    // ── AC1 — "Limpiar consolidado" SIEMPRE regenera ────────────────────────────────────────────

    [Fact]
    public async Task AC1_ConsolidadoVigenteSourceUser_Limpiar_LoDescartaYRegeneraConSourceSystem()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instance = MatriculaInstanceConFur(id, tenantId);
        AddAttachment(instance, "consolidado", "consolidado_user.pdf", "user", DateTimeOffset.UtcNow);
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var generar = new GenerarConsolidadoHandler(_repo, _merger, _storage);
        var handler = new LimpiarConsolidadoHandler(_repo, generar);

        var (result, error) = await handler.HandleAsync(id, tenantId, userId, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Regenerado.Should().BeTrue("AC1: Limpiar SIEMPRE regenera, sin importar el Source vigente");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "consolidado")
            .Which.Source.Should().Be("system");
        instance.Attachments.Should().NotContain(a => a.Filename == "consolidado_user.pdf");
    }

    [Fact]
    public async Task AC1_ConsolidadoVigenteSourceSystem_Limpiar_TambienRegenera()
    {
        // "Limpiar" también tiene sentido sobre un consolidado del sistema (forzar regeneración).
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstanceConFur(id, tenantId);
        AddAttachment(instance, "consolidado", "consolidado_viejo.pdf", "system", DateTimeOffset.UtcNow.AddDays(-1));
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var generar = new GenerarConsolidadoHandler(_repo, _merger, _storage);
        var handler = new LimpiarConsolidadoHandler(_repo, generar);

        var (result, error) = await handler.HandleAsync(id, tenantId, userId: null, CancellationToken.None);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        instance.Attachments.Should().NotContain(a => a.Filename == "consolidado_viejo.pdf");
    }

    [Fact]
    public async Task AC1_TramiteMigradoFinal_Limpiar_RespetaElGuardDeSoloLectura()
    {
        // El guard existente de GenerarConsolidadoHandler (migrado + estado final) no lo rompe esta HU.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstanceConFur(id, tenantId, migrado: true, status: TramiteEstado.Aprobado);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var generar = new GenerarConsolidadoHandler(_repo, _merger, _storage);
        var handler = new LimpiarConsolidadoHandler(_repo, generar);

        var (result, error) = await handler.HandleAsync(id, tenantId, userId: null, CancellationToken.None);

        error.Should().Be("migrado_solo_lectura");
        result.Should().BeNull();
        _storage.Saved.Should().BeEmpty();
    }

    // ── AC4 — trazabilidad de "Limpiar consolidado" ─────────────────────────────────────────────

    [Fact]
    public async Task AC4_Limpiar_RegistraEventoConUsuarioFechaYHora()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instance = MatriculaInstanceConFur(id, tenantId);
        AddAttachment(instance, "consolidado", "consolidado_user.pdf", "user", DateTimeOffset.UtcNow);
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var generar = new GenerarConsolidadoHandler(_repo, _merger, _storage);
        var handler = new LimpiarConsolidadoHandler(_repo, generar);
        var antes = DateTimeOffset.UtcNow;

        var (result, error) = await handler.HandleAsync(id, tenantId, userId, CancellationToken.None);

        error.Should().BeNull();
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e =>
                e.Tipo == "consolidado_limpiado_admin"
                && e.TenantId == tenantId
                && e.ProcedureInstanceId == id
                && e.CreatedBy == userId
                && e.CreatedAt >= antes),
            Arg.Any<CancellationToken>());
        await _repo.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── AC3 — "Cargar consolidado externo" ──────────────────────────────────────────────────────

    private static CargarConsolidadoExternoInput PdfInput(
        string filename = "externo.pdf", string mime = "application/pdf", long size = 100) =>
        new(filename, mime, size, new MemoryStream(Encoding.UTF8.GetBytes("%PDF-externo")));

    [Fact]
    public async Task AC3_CargaUnPdfExterno_QuedaRegistradoConSourceUser()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instance = MatriculaInstanceConFur(id, tenantId);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var handler = new CargarConsolidadoExternoHandler(_repo, _storage);

        var (result, error) = await handler.HandleAsync(id, tenantId, PdfInput(), userId, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Tipo.Should().Be("consolidado");
        var attachment = instance.Attachments.Should().ContainSingle(a => a.Tipo == "consolidado").Subject;
        attachment.Source.Should().Be("user");
        attachment.UploadedBy.Should().Be(userId);
        attachment.Filename.Should().Be("externo.pdf");
        _storage.Saved.Should().ContainSingle();
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_CargaExterna_ReemplazaCualquierConsolidadoVigente_SinImportarSuSource()
    {
        // Acción explícita del admin: reemplaza incluso un consolidado previo Source="system".
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstanceConFur(id, tenantId);
        AddAttachment(instance, "consolidado", "consolidado_previo.pdf", "system", DateTimeOffset.UtcNow.AddHours(-1));
        _storage.Files[instance.Attachments.First(a => a.Tipo == "consolidado").StoragePath]
            = Encoding.UTF8.GetBytes("previo");
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var handler = new CargarConsolidadoExternoHandler(_repo, _storage);

        var (result, error) = await handler.HandleAsync(id, tenantId, PdfInput(), userId: null, CancellationToken.None);

        error.Should().BeNull();
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "consolidado")
            .Which.Filename.Should().Be("externo.pdf");
        instance.Attachments.Should().NotContain(a => a.Filename == "consolidado_previo.pdf");
    }

    [Fact]
    public async Task AC3_CargaExterna_PrevaleceSobreRegeneracionAutomaticaPosterior()
    {
        // Integra AC3 con la protección de AC2: tras la carga externa, una regeneración normal
        // (sin bypass) NO debe sobrescribir lo que el admin acaba de cargar.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstanceConFur(id, tenantId);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var cargar = new CargarConsolidadoExternoHandler(_repo, _storage);
        var (cargado, cargarError) = await cargar.HandleAsync(id, tenantId, PdfInput(), userId: null, CancellationToken.None);
        cargarError.Should().BeNull();

        foreach (var att in instance.Attachments)
            _storage.Files.TryAdd(att.StoragePath, Encoding.UTF8.GetBytes(att.Filename));
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var generar = new GenerarConsolidadoHandler(_repo, _merger, _storage);

        var (result, error) = await generar.HandleAsync(id, tenantId, userId: null, force: true, CancellationToken.None);

        error.Should().BeNull();
        result!.Regenerado.Should().BeFalse("el consolidado recién cargado (Source=\"user\") está protegido");
        result.Document.AttachmentId.Should().Be(cargado!.AttachmentId);
    }

    [Theory]
    [InlineData(null, "application/pdf", 100L, "missing_file")]
    [InlineData("x.png", "image/png", 100L, "invalid_mime")]
    public async Task AC3_ValidacionesDeArchivo_RetornanElErrorEsperado(
        string? filename, string mime, long size, string errorEsperado)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstanceConFur(id, tenantId);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var handler = new CargarConsolidadoExternoHandler(_repo, _storage);

        var content = filename is null ? Stream.Null : new MemoryStream(Encoding.UTF8.GetBytes("data"));
        var input = new CargarConsolidadoExternoInput(filename ?? "x.pdf", mime, size, content);
        if (filename is null)
            input = input with { SizeBytes = 0 };

        var (result, error) = await handler.HandleAsync(id, tenantId, input, userId: null, CancellationToken.None);

        error.Should().Be(errorEsperado);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC3_ArchivoDemasiadoGrande_RetornaFileTooLarge()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstanceConFur(id, tenantId);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var handler = new CargarConsolidadoExternoHandler(_repo, _storage);

        var input = PdfInput(size: CargarConsolidadoExternoHandler.MaxSizeBytes + 1);

        var (result, error) = await handler.HandleAsync(id, tenantId, input, userId: null, CancellationToken.None);

        error.Should().Be("file_too_large");
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC3_InstanciaNoExiste_RetornaNotFound()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);
        var handler = new CargarConsolidadoExternoHandler(_repo, _storage);

        var (result, error) = await handler.HandleAsync(id, tenantId, PdfInput(), userId: null, CancellationToken.None);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC3_TramiteMigradoFinal_RespetaElGuardDeSoloLectura()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstanceConFur(id, tenantId, migrado: true, status: TramiteEstado.Anulado);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var handler = new CargarConsolidadoExternoHandler(_repo, _storage);

        var (result, error) = await handler.HandleAsync(id, tenantId, PdfInput(), userId: null, CancellationToken.None);

        error.Should().Be("migrado_solo_lectura");
        result.Should().BeNull();
        _storage.Saved.Should().BeEmpty();
    }

    // ── AC4 — trazabilidad de "Cargar consolidado externo" ──────────────────────────────────────

    [Fact]
    public async Task AC4_CargarExterno_RegistraEventoConUsuarioFechaYHora()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instance = MatriculaInstanceConFur(id, tenantId);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var handler = new CargarConsolidadoExternoHandler(_repo, _storage);
        var antes = DateTimeOffset.UtcNow;

        var (result, error) = await handler.HandleAsync(id, tenantId, PdfInput(), userId, CancellationToken.None);

        error.Should().BeNull();
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e =>
                e.Tipo == "consolidado_cargado_admin"
                && e.TenantId == tenantId
                && e.ProcedureInstanceId == id
                && e.CreatedBy == userId
                && e.CreatedAt >= antes),
            Arg.Any<CancellationToken>());
    }
}

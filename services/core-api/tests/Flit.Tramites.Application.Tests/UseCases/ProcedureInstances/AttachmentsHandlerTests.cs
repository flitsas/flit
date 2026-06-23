using System.Text;
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

public sealed class AttachmentsHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeStorage _storage = new();
    private readonly UploadAttachmentHandler _upload;
    private readonly ListAttachmentsHandler _list;
    private readonly DeleteAttachmentHandler _delete;
    private readonly DownloadAttachmentHandler _download;
    private readonly GetChecklistHandler _checklist;

    public AttachmentsHandlerTests()
    {
        _upload = new UploadAttachmentHandler(_repo, _storage);
        _list = new ListAttachmentsHandler(_repo);
        _delete = new DeleteAttachmentHandler(_repo, _storage);
        _download = new DownloadAttachmentHandler(_repo, _storage);
        _checklist = new GetChecklistHandler(_repo);
    }

    /// <summary>Storage en memoria: registra saves/deletes y devuelve un hash determinista.</summary>
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

        public void Delete(string storagePath) => Deleted.Add(storagePath);

        public Stream? OpenRead(string storagePath) =>
            Contents.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null;
    }

    private static ProcedureInstance Instance(
        Guid id, Guid tenantId,
        string modalidad = "matricula_inicial",
        string status = ProcedureInstanceStatus.Draft,
        string? tipologia = null,
        string checklistEstado = "{}") =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = modalidad,
            TipologiaCodigo = tipologia,
            ChecklistEstado = checklistEstado,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static UploadAttachmentInput Pdf(
        string tipo = "factura", string mime = "application/pdf", long size = 1024, string name = "doc.pdf") =>
        new(tipo, name, mime, size, new MemoryStream(Encoding.UTF8.GetBytes("hello-pdf-content")));

    // ── Validación mime/size/tipo ─────────────────────────────────────────────

    [Fact]
    public async Task Upload_InvalidTipo_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var (result, error) = await _upload.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), Pdf(tipo: "no_existe"), null, ct);

        error.Should().Be("invalid_tipo");
        result.Should().BeNull();
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_InvalidMime_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, error) = await _upload.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), Pdf(mime: "text/plain"), null, ct);

        error.Should().Be("invalid_mime");
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_TooLarge_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, error) = await _upload.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), Pdf(size: AttachmentRules.MaxSizeBytes + 1), null, ct);

        error.Should().Be("file_too_large");
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_MissingFile_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var input = new UploadAttachmentInput("factura", "x.pdf", "application/pdf", 0, Stream.Null);
        var (_, error) = await _upload.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), input, null, ct);

        error.Should().Be("missing_file");
    }

    // ── 404 / 409 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_InstanceNotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _upload.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Pdf(), null, ct);

        error.Should().Be("not_found");
        _storage.Saved.Should().BeEmpty();
    }

    [Theory]
    [InlineData("submitted")]
    [InlineData("completed")]
    public async Task Upload_NotDraft_Returns409(string status)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(Instance(id, tenant, status: status));

        var (_, error) = await _upload.HandleAsync(id, tenant, Pdf(), null, ct);

        error.Should().Be("not_draft");
        _storage.Saved.Should().BeEmpty();
    }

    // ── Happy path + checklist auto-marca ─────────────────────────────────────

    [Fact]
    public async Task Upload_HappyPath_PersistsAndReturnsDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "matricula_inicial");
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _upload.HandleAsync(id, tenant, Pdf(tipo: "factura"), null, ct);

        error.Should().BeNull();
        result!.Tipo.Should().Be("factura");
        result.Sha256.Should().Be("deadbeef");
        result.Source.Should().Be("user");
        instance.Attachments.Should().ContainSingle();
        _storage.Saved.Should().ContainSingle();
        // El adjunto NUEVO se marca Added explícito → INSERT (PK store-generated con Id ya seteado).
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceAttachment>(a => a.Tipo == "factura"));
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Upload_FacturaInMatriculaInicial_AutoMarksChecklistItem()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        // tipología matricula_inicial tiene ítem "factura" con docTipo "factura"
        var instance = Instance(id, tenant, tipologia: TramiteTipologiaCatalog.CodigoMatriculaInicial);
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _upload.HandleAsync(id, tenant, Pdf(tipo: "factura"), null, ct);

        error.Should().BeNull();
        instance.ChecklistEstado.Should().Contain("\"factura\":true");
    }

    [Fact]
    public async Task Upload_DocWithoutMatchingItem_DoesNotMarkChecklist()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, tipologia: TramiteTipologiaCatalog.CodigoMatriculaInicial);
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);

        // "compraventa" es válido (whitelist) pero no es docTipo de ningún ítem de
        // matrícula inicial ⇒ checklist_estado queda {}
        var (_, error) = await _upload.HandleAsync(id, tenant, Pdf(tipo: "compraventa"), null, ct);

        error.Should().BeNull();
        instance.ChecklistEstado.Should().Be("{}");
    }

    [Fact]
    public async Task Upload_AutoMarkResolvesTipologiaByModalidad_WhenCodigoNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        // sin tipologia_codigo; modalidad_entrada == "matricula_inicial" resuelve la tipología
        var instance = Instance(id, tenant, modalidad: TramiteTipologiaCatalog.CodigoMatriculaInicial, tipologia: null);
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _upload.HandleAsync(id, tenant, Pdf(tipo: "soat"), null, ct);

        error.Should().BeNull();
        instance.ChecklistEstado.Should().Contain("\"soat\":true");
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _list.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task List_ReturnsUploadedAttachments()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(), TenantId = tenant, ProcedureInstanceId = id,
            Tipo = "factura", Filename = "f.pdf", Mimetype = "application/pdf",
            SizeBytes = 10, Sha256 = "abc", StoragePath = "p", Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _list.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Attachments.Should().ContainSingle().Which.Tipo.Should().Be("factura");
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_HappyPath_RemovesFromFsAndDb_Returns204()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = attachmentId, ProcedureInstanceId = id, Tipo = "factura",
            StoragePath = "some/path.pdf", UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);

        var error = await _delete.HandleAsync(id, tenant, attachmentId, ct);

        error.Should().BeNull();
        _storage.Deleted.Should().ContainSingle().Which.Should().Be("some/path.pdf");
        instance.Attachments.Should().BeEmpty();
        _repo.Received(1).RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>());
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Delete_UnmarksChecklistItem_WhenNoOtherAttachmentOfTipo()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        // traspaso: el ítem "rtm" (docTipo "rtm") quedó auto-marcado por una subida previa.
        var instance = Instance(id, tenant, tipologia: TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.ChecklistEstado = "{\"rtm\":true}";
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = attachmentId, ProcedureInstanceId = id, Tipo = "rtm",
            StoragePath = "p/rtm.pdf", UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);

        var error = await _delete.HandleAsync(id, tenant, attachmentId, ct);

        error.Should().BeNull();
        // Sin otro adjunto "rtm", el ítem deja de estar satisfecho.
        instance.ChecklistEstado.Should().NotContain("rtm");
    }

    [Fact]
    public async Task Delete_KeepsChecklistMark_WhenAnotherAttachmentOfTipoRemains()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var instance = Instance(id, tenant, tipologia: TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.ChecklistEstado = "{\"rtm\":true}";
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = attachmentId, ProcedureInstanceId = id, Tipo = "rtm",
            StoragePath = "p/rtm-1.pdf", UploadedAt = DateTimeOffset.UtcNow,
        });
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(), ProcedureInstanceId = id, Tipo = "rtm",
            StoragePath = "p/rtm-2.pdf", UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);

        var error = await _delete.HandleAsync(id, tenant, attachmentId, ct);

        error.Should().BeNull();
        // Queda otro adjunto "rtm" → el ítem sigue satisfecho.
        instance.ChecklistEstado.Should().Contain("\"rtm\":true");
    }

    [Fact]
    public async Task Delete_AttachmentNotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var error = await _delete.HandleAsync(id, tenant, Guid.NewGuid(), ct);

        error.Should().Be("attachment_not_found");
        _storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_NotDraft_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(Instance(id, tenant, status: "submitted"));

        var error = await _delete.HandleAsync(id, tenant, Guid.NewGuid(), ct);

        error.Should().Be("not_draft");
    }

    // ── Download (DF-1) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_HappyPath_StreamsBytesWithMimeAndFilename()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);
        // Sube un adjunto real para que el FakeStorage guarde su contenido y OpenRead lo devuelva.
        await _upload.HandleAsync(id, tenant, Pdf(tipo: "factura", name: "doc.pdf"), null, ct);
        var attachment = instance.Attachments.First();

        var (result, error) = await _download.HandleAsync(id, tenant, attachment.Id, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Mimetype.Should().Be("application/pdf");
        result.Filename.Should().Be("doc.pdf");
        using var ms = new MemoryStream();
        await result.Content.CopyToAsync(ms, ct);
        Encoding.UTF8.GetString(ms.ToArray()).Should().Be("hello-pdf-content");
    }

    [Fact]
    public async Task Download_InstanceNotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _download.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Download_AttachmentNotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (_, error) = await _download.HandleAsync(id, tenant, Guid.NewGuid(), ct);

        error.Should().Be("not_found");
    }

    // ── Checklist ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Checklist_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _checklist.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Checklist_ComputesSatisfiedFromUploadedDocs()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, tipologia: TramiteTipologiaCatalog.CodigoMatriculaInicial);
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(), Tipo = "factura", StoragePath = "p", UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _checklist.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Items.Should().Contain(i => i.Key == "factura" && i.Satisfied);
        // soat ahora es opcional: presente pero no bloquea
        result.Items.Should().Contain(i => i.Key == "soat" && !i.Satisfied);
        result.FaltanObligatorios.Should().NotContain("soat");
        // aduana e impronta son los obligatorios restantes (junto a factura ya subida)
        result.Items.Should().Contain(i => i.Key == "aduana" && !i.Satisfied);
        result.FaltanObligatorios.Should().Contain("aduana");
        result.FaltanObligatorios.Should().Contain("impronta");
        result.Completo.Should().BeFalse();
    }

    [Fact]
    public async Task Checklist_AllObligatoriosSatisfied_IsCompleto()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        // matricula_inicial: obligatorios = factura, aduana, impronta (docTipo). SOAT es opcional.
        var instance = Instance(
            id, tenant,
            tipologia: TramiteTipologiaCatalog.CodigoMatriculaInicial);
        foreach (var t in new[] { "factura", "aduana", "impronta" })
        {
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(), Tipo = t, StoragePath = "p", UploadedAt = DateTimeOffset.UtcNow,
            });
        }
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _checklist.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.FaltanObligatorios.Should().BeEmpty();
        result.Completo.Should().BeTrue();
    }

    [Fact]
    public async Task Checklist_UnknownTipologia_Returns422()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, modalidad: "desconocida", tipologia: "no_existe");
        _repo.GetByIdWithAttachmentsAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _checklist.HandleAsync(id, tenant, ct);

        error.Should().Be("tipologia_not_found");
    }
}

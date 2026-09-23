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
/// HU #12785 — ruta única de entrega del consolidado (<see cref="EntregarConsolidadoHandler"/>), para
/// los dos PDF (D2): wizard (<c>consolidado</c>) y maestro (<c>consolidado_maestro</c>). Los generadores
/// son los REALES (<see cref="GenerarConsolidadoHandler"/> / <see cref="GenerarConsolidadoMaestroHandler"/>)
/// sobre un repositorio sustituto y un storage en memoria: se verifica que la entrega delega en ellos y
/// no duplica la generación.
/// <para>Uso de ejemplo:
/// <c>var (r, e) = await handler.HandleAsync(new EntregarConsolidadoRequest(id, tenantId, ConsolidadoEntregaTipo.Maestro));</c>
/// ⇒ <c>r.Regenerado</c> indica si se reconstruyó; <c>r.DefinitivoPorEstadoFinal</c> si es el definitivo.</para>
/// </summary>
public sealed class EntregarConsolidadoHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeStorage _storage = new();
    private readonly EntregarConsolidadoHandler _handler;

    public EntregarConsolidadoHandlerTests()
    {
        var merger = new FakeMerger();
        _handler = new EntregarConsolidadoHandler(
            _repo,
            new GenerarConsolidadoHandler(_repo, merger, _storage),
            new GenerarConsolidadoMaestroHandler(_repo, merger, _storage));
    }

    public static TheoryData<ConsolidadoEntregaTipo> AmbosPdf() =>
        new() { ConsolidadoEntregaTipo.Wizard, ConsolidadoEntregaTipo.Maestro };

    // ── AC1 — bandera abajo reconstruye ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AmbosPdf))]
    public async Task AC1_BanderaAbajo_NoFinal_Reconstruye_SubeBandera_RegeneradoTrue(ConsolidadoEntregaTipo tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var previo = AddAttachment(instance, TipoAdjunto(tipo), "previo.pdf", "system");
        SetBandera(instance, tipo, false);
        Wire(instance);

        var (result, error) = await _handler.HandleAsync(new EntregarConsolidadoRequest(instance.Id, instance.TenantId, tipo), ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        result.Modo.Should().Be(ConsolidadoEntregaModos.Regenerado);
        result.DefinitivoPorEstadoFinal.Should().BeFalse();
        result.Document.Tipo.Should().Be(TipoAdjunto(tipo));
        result.Document.AttachmentId.Should().NotBe(previo.Id, "el PDF desactualizado se reemplaza por uno nuevo");
        Bandera(instance, tipo).Should().BeTrue("la reconstrucción deja el consolidado vigente");
        _storage.Saved.Should().ContainSingle();
    }

    [Fact]
    public async Task AC1_Contrato_LaEntregaDelegaEnElGenerador_EventoConsolidadoGenerado()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Borrador);
        AddAttachment(instance, "consolidado", "previo.pdf", "system");
        instance.ConsolidadoWizardVigente = false;
        Wire(instance);

        await _handler.HandleAsync(new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Wizard), ct);

        instance.Events.Should().ContainSingle(e => e.Tipo == "consolidado_generado",
            "la entrega no reimplementa la generación: el evento lo escribe GenerarConsolidadoHandler");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── AC2 — bandera arriba no reconstruye ─────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AmbosPdf))]
    public async Task AC2_BanderaArriba_DevuelveCacheado_RegeneradoFalse_SinEscrituraEnStorage(ConsolidadoEntregaTipo tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var vigente = AddAttachment(instance, TipoAdjunto(tipo), "vigente.pdf", "system");
        SetBandera(instance, tipo, true);
        Wire(instance);

        var (result, error) = await _handler.HandleAsync(new EntregarConsolidadoRequest(instance.Id, instance.TenantId, tipo), ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeFalse();
        result.Modo.Should().Be(ConsolidadoEntregaModos.Vigente);
        result.Document.AttachmentId.Should().Be(vigente.Id);
        _storage.Saved.Should().BeEmpty();
        _storage.Deleted.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── AC3 — estado final no regenera ──────────────────────────────────────────────────────────

    public static TheoryData<string, ConsolidadoEntregaTipo> FinalesPorPdf()
    {
        var data = new TheoryData<string, ConsolidadoEntregaTipo>();
        foreach (var estado in new[] { TramiteEstado.Aprobado, TramiteEstado.Anulado, TramiteEstado.Revocado })
        {
            data.Add(estado, ConsolidadoEntregaTipo.Wizard);
            data.Add(estado, ConsolidadoEntregaTipo.Maestro);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FinalesPorPdf))]
    public async Task AC3_EstadoFinal_BanderaAbajo_DevuelveExistente_SinRegenerar_MarcadoDefinitivo(string estado, ConsolidadoEntregaTipo tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(estado);
        var existente = AddAttachment(instance, TipoAdjunto(tipo), "definitivo.pdf", "system");
        SetBandera(instance, tipo, false);
        Wire(instance);

        var (result, error) = await _handler.HandleAsync(new EntregarConsolidadoRequest(instance.Id, instance.TenantId, tipo), ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeFalse();
        result.DefinitivoPorEstadoFinal.Should().BeTrue();
        result.Modo.Should().Be(ConsolidadoEntregaModos.DefinitivoEstadoFinal);
        result.Document.AttachmentId.Should().Be(existente.Id);
        Bandera(instance, tipo).Should().BeFalse("la entrega en estado final no toca la bandera");
        _storage.Saved.Should().BeEmpty();
        _storage.Deleted.Should().BeEmpty();
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_EstadoFinal_ConForce_TampocoRegenera()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Aprobado);
        var existente = AddAttachment(instance, "consolidado_maestro", "definitivo.pdf", "system");
        Wire(instance);

        var (result, _) = await _handler.HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro, Force: true), ct);

        result!.Document.AttachmentId.Should().Be(existente.Id);
        result.DefinitivoPorEstadoFinal.Should().BeTrue();
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_EstadoFinal_SinPdf_ConsolidadoNoGenerado_SinGenerarUnoNuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Aprobado);
        Wire(instance);

        var (result, error) = await _handler.HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Wizard), ct);

        result.Should().BeNull();
        error.Should().Be(EntregarConsolidadoHandler.ConsolidadoNoGenerado);
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public void AC3_Contrato_ElCampoNuevoEsOpcional_LasRutasDeGeneracionLoDejanEnFalse()
    {
        var dto = new ConsolidadoDocumentDto(Guid.NewGuid(), "consolidado", "c.pdf", "sha");

        var result = new GenerarConsolidadoResult(dto);

        result.DefinitivoPorEstadoFinal.Should().BeFalse();
        result.Modo.Should().BeNull();
        result.Regenerado.Should().BeTrue("el default histórico del record no cambia");
    }

    // ── AC4 — Source=user no se pisa ────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AmbosPdf))]
    public async Task AC4_SourceUser_ConForce_DevuelveElPdfDelSuperAdmin(ConsolidadoEntregaTipo tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var cargado = AddAttachment(instance, TipoAdjunto(tipo), "cargado_admin.pdf", "user");
        SetBandera(instance, tipo, false);
        Wire(instance);

        var (result, error) = await _handler.HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, tipo, Force: true), ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().Be(cargado.Id);
        result.Regenerado.Should().BeFalse();
        result.Modo.Should().Be(ConsolidadoEntregaModos.CargadoPorUsuario);
        instance.Attachments.Should().Contain(cargado, "el PDF del SuperAdmin no se borra");
        _storage.Saved.Should().BeEmpty();
        _storage.Deleted.Should().BeEmpty();
    }

    // ── AC5 — migrado V1 en estado final ────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AmbosPdf))]
    public async Task AC5_MigradoV1_EstadoFinal_ModoMigradoSoloLectura_SinRegenerar(ConsolidadoEntregaTipo tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Aprobado);
        instance.IsMigrated = true;
        var v1 = AddAttachment(instance, TipoAdjunto(tipo), "v1.pdf", "migration");
        SetBandera(instance, tipo, false);
        Wire(instance);

        var (result, error) = await _handler.HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, tipo, Force: true), ct);

        error.Should().BeNull();
        result!.Modo.Should().Be(ConsolidadoEntregaModos.MigradoSoloLectura);
        result.Document.AttachmentId.Should().Be(v1.Id);
        result.Regenerado.Should().BeFalse();
        result.DefinitivoPorEstadoFinal.Should().BeTrue();
        _storage.Saved.Should().BeEmpty();
        _storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task AC5_MigradoV1_EstadoFinal_SinPdf_ErrorMigradoSoloLectura()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Anulado);
        instance.IsMigrated = true;
        Wire(instance);

        var (result, error) = await _handler.HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro), ct);

        result.Should().BeNull();
        error.Should().Be(ConsolidadoEntregaModos.MigradoSoloLectura);
        _storage.Saved.Should().BeEmpty();
    }

    // ── AC6 — sin consolidado previo, trámite no final ──────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AmbosPdf))]
    public async Task AC6_SinConsolidadoPrevio_NoFinal_GeneraPorPrimeraVez(ConsolidadoEntregaTipo tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Borrador);
        Wire(instance);

        var (result, error) = await _handler.HandleAsync(new EntregarConsolidadoRequest(instance.Id, instance.TenantId, tipo), ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        result.Modo.Should().Be(ConsolidadoEntregaModos.Regenerado);
        instance.Attachments.Should().ContainSingle(a => a.Tipo == TipoAdjunto(tipo));
        Bandera(instance, tipo).Should().BeTrue();
    }

    // ── Bordes de la ruta de entrega ────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoEncontrado_DevuelveNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _handler.HandleAsync(
            new EntregarConsolidadoRequest(Guid.NewGuid(), Guid.NewGuid(), ConsolidadoEntregaTipo.Wizard), ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    [Fact]
    public async Task SoloLectura_BanderaAbajo_DevuelveElAdjuntoTalCual_SinRegenerar()
    {
        // Vista read-only del OT tras radicar ante Quipux (HU #12787 AC2): el maestro radicado se
        // sirve tal cual aunque una invalidación posterior haya bajado la bandera.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var radicado = AddAttachment(instance, "consolidado_maestro", "radicado.pdf", "system");
        instance.ConsolidadoMaestroVigente = false;
        Wire(instance);

        var (result, error) = await _handler.HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro, SoloLectura: true), ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().Be(radicado.Id);
        result.Modo.Should().Be(ConsolidadoEntregaModos.SoloLectura);
        result.Regenerado.Should().BeFalse();
        _storage.Saved.Should().BeEmpty();
        _storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task SoloLectura_SinPdf_ConsolidadoNoGenerado()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        Wire(instance);

        var (_, error) = await _handler.HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro, SoloLectura: true), ct);

        error.Should().Be(EntregarConsolidadoHandler.ConsolidadoNoGenerado);
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task ErrorDelGenerador_ViajaTalCual()
    {
        // Wizard sin FUR y sin regenerador inyectado ⇒ el generador responde fur_requerido.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Borrador, conFur: false);
        Wire(instance);

        var (result, error) = await _handler.HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Wizard), ct);

        result.Should().BeNull();
        error.Should().Be(SubmitGate.FurRequerido);
    }

    // ── Infraestructura del test ────────────────────────────────────────────────────────────────

    private static string TipoAdjunto(ConsolidadoEntregaTipo tipo) =>
        tipo == ConsolidadoEntregaTipo.Maestro ? "consolidado_maestro" : "consolidado";

    private static void SetBandera(ProcedureInstance instance, ConsolidadoEntregaTipo tipo, bool valor)
    {
        if (tipo == ConsolidadoEntregaTipo.Maestro)
            instance.ConsolidadoMaestroVigente = valor;
        else
            instance.ConsolidadoWizardVigente = valor;
    }

    private static bool Bandera(ProcedureInstance instance, ConsolidadoEntregaTipo tipo) =>
        tipo == ConsolidadoEntregaTipo.Maestro ? instance.ConsolidadoMaestroVigente : instance.ConsolidadoWizardVigente;

    private void Wire(ProcedureInstance instance)
    {
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
    }

    private ProcedureInstance Instancia(string estado, bool conFur = true)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-012785",
            Status = estado,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (conFur)
            AddAttachment(instance, "fur", "fur.pdf", "system");
        AddAttachment(instance, "factura", "factura.pdf", "user");
        return instance;
    }

    private ProcedureInstanceAttachment AddAttachment(ProcedureInstance instance, string tipo, string filename, string source)
    {
        var path = $"{instance.Id:D}/{tipo}-{Guid.NewGuid():N}";
        var content = System.Text.Encoding.UTF8.GetBytes($"%PDF-{filename}");
        _storage.Files[path] = content;
        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = filename,
            Mimetype = "application/pdf",
            SizeBytes = content.Length,
            Sha256 = $"sha-{tipo}",
            StoragePath = path,
            Source = source,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        instance.Attachments.Add(attachment);
        return attachment;
    }

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
            var path = $"{procedureInstanceId:D}/{tipo}_saved_{Saved.Count}";
            Files[path] = bytes;
            Saved.Add(path);
            return new StoredFile(path, $"sha-{tipo}-nuevo", bytes.Length);
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
            Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }
}

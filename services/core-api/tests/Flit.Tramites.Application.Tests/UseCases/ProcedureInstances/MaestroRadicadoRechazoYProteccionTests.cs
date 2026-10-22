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
/// Última ronda de la re-review de la Épica #12760 sobre el maestro radicado:
/// <list type="bullet">
///   <item><b>R1 (security M-N1)</b> — el gestor no borra consolidados ni adjuntos referenciados por Quipux
///   (<see cref="DeleteAttachmentHandler.AdjuntoProtegido"/>); el resto de tipos se borra como antes.</item>
///   <item><b>R2 (code-review N1)</b> — un rechazo de Quipux deja de fijar el maestro: el worker y el POST OT
///   regeneran, y la fila rechazada (protegida) se conserva. Incluye la carrera trámite-rechazado /
///   submission aún <c>registrado</c>.</item>
///   <item><b>R4 (N5)</b> — con dos filas <c>consolidado_maestro</c> decide la más reciente
///   (regeneración anticipada, <see cref="RegenerarConsolidadoAnticipadoHandler"/>) y el listado del OT deja una por
///   tipo (<see cref="ConsolidadoListado.UnoPorTipo"/>).</item>
/// </list>
/// <para>Uso de ejemplo: <c>await new DeleteAttachmentHandler(repo, storage, maestroRadicado: lookup)
/// .HandleAsync(id, tenant, maestroId, ct)</c> ⇒ <c>"adjunto_protegido"</c>.</para>
/// </summary>
public sealed class MaestroRadicadoRechazoYProteccionTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IMaestroRadicadoLookup _lookup = Substitute.For<IMaestroRadicadoLookup>();
    private readonly MemStorage _storage = new();
    private readonly FakeMerger _merger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public MaestroRadicadoRechazoYProteccionTests()
    {
        _lookup.AttachmentsProtegidosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());
        _lookup.AttachmentRadicadoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
    }

    private DeleteAttachmentHandler Delete() => new(_repo, _storage, imprintAudit: null, maestroRadicado: _lookup);

    private GenerarConsolidadoMaestroHandler Maestro() => new(_repo, _merger, _storage, maestroRadicado: _lookup);

    private RegenerarConsolidadoAnticipadoHandler Worker() =>
        new(_repo, new GenerarConsolidadoHandler(_repo, _merger, _storage), Maestro(), logger: null, bitacora: null, maestroRadicado: _lookup);

    // ── R1 — DELETE de adjuntos ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("consolidado")]
    [InlineData("consolidado_maestro")]
    [InlineData("CONSOLIDADO_MAESTRO")]
    public async Task R1_GestorEnSubsanacion_NoPuedeBorrarConsolidados_AdjuntoProtegido(string tipo)
    {
        var instance = Instancia(TramiteEstado.Rechazado);
        instance.SubsanacionActiva = true; // PermiteEdicionDatos = true: el gate de estado no lo frena
        var consolidado = Adjuntar(instance, tipo);

        var error = await Delete().HandleAsync(instance.Id, instance.TenantId, consolidado.Id, Ct);

        error.Should().Be(DeleteAttachmentHandler.AdjuntoProtegido).And.Be("adjunto_protegido");
        instance.Attachments.Should().Contain(consolidado);
        _storage.Deleted.Should().BeEmpty("el binario del documento del sistema no se toca");
        _repo.DidNotReceive().RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task R1_AdjuntoReferenciadoPorUnaRadicacion_AdjuntoProtegido_AunqueSuTipoNoSeaConsolidado()
    {
        var instance = Instancia(TramiteEstado.Rechazado);
        instance.SubsanacionActiva = true;
        var referenciado = Adjuntar(instance, "factura");
        Proteger(instance, referenciado.Id);

        var error = await Delete().HandleAsync(instance.Id, instance.TenantId, referenciado.Id, Ct);

        error.Should().Be(DeleteAttachmentHandler.AdjuntoProtegido);
        _storage.Deleted.Should().BeEmpty();
        _repo.DidNotReceive().RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>());
    }

    [Theory]
    [InlineData("factura")]
    [InlineData("soat")]
    [InlineData("fur")]
    public async Task R1_OtrosTipos_SeBorranComoAntes(string tipo)
    {
        var instance = Instancia(TramiteEstado.Rechazado);
        instance.SubsanacionActiva = true;
        var otroMaestro = Adjuntar(instance, "consolidado_maestro");
        Proteger(instance, otroMaestro.Id);
        var adjunto = Adjuntar(instance, tipo);

        var error = await Delete().HandleAsync(instance.Id, instance.TenantId, adjunto.Id, Ct);

        error.Should().BeNull();
        instance.Attachments.Should().NotContain(adjunto).And.Contain(otroMaestro);
        _storage.Deleted.Should().Equal(adjunto.StoragePath);
        _repo.Received(1).RemoveAttachment(adjunto);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task R1_ElGateDeEstadoSigueAntes_NotDraftSinConsultarLaProteccion()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var maestro = Adjuntar(instance, "consolidado_maestro");

        var error = await Delete().HandleAsync(instance.Id, instance.TenantId, maestro.Id, Ct);

        error.Should().Be("not_draft", "el contrato previo (409 not_draft) no cambia fuera de edición");
        await _lookup.DidNotReceive().AttachmentsProtegidosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task R1_SinLookupCableado_LosConsolidadosSiguenProtegidosPorTipo()
    {
        var instance = Instancia(TramiteEstado.Borrador);
        var consolidado = Adjuntar(instance, "consolidado");

        var error = await new DeleteAttachmentHandler(_repo, _storage).HandleAsync(instance.Id, instance.TenantId, consolidado.Id, Ct);

        error.Should().Be(DeleteAttachmentHandler.AdjuntoProtegido);
    }

    // ── R2 — rechazo de Quipux: ya no fijo ──────────────────────────────────────────────────────

    [Fact]
    public async Task R2_Rechazado_Worker_RegeneraUnMaestroNuevo_YConservaLaFilaRechazada()
    {
        // Submission rechazada: el lookup ya no la devuelve como fija, pero sí como protegida.
        var instance = Instancia(TramiteEstado.Rechazado);
        var rechazado = Adjuntar(instance, "consolidado_maestro", subidoHace: TimeSpan.FromHours(1));
        instance.InvalidarConsolidados();
        Proteger(instance, rechazado.Id);

        var resultado = await Worker().HandleAsync(instance.TenantId, instance.Id, TipoConsolidado.Maestro, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.Regenerado);
        _storage.Saved.Should().ContainSingle("se creó un maestro nuevo");
        instance.Attachments.Where(a => a.Tipo == "consolidado_maestro").Should().HaveCount(2);
        instance.Attachments.Should().Contain(rechazado, "la fila que referencia la submission rechazada se conserva");
        _storage.Files.Should().ContainKey(rechazado.StoragePath);
        _repo.DidNotReceive().RemoveAttachment(rechazado);
    }

    [Fact]
    public async Task R2_Rechazado_PostOt_RegeneraUnMaestroNuevo_YConservaLaFilaRechazada()
    {
        var instance = Instancia(TramiteEstado.Rechazado);
        var rechazado = Adjuntar(instance, "consolidado_maestro", subidoHace: TimeSpan.FromHours(1));
        instance.InvalidarConsolidados();
        Proteger(instance, rechazado.Id);

        var (result, error) = await Maestro().HandleRespetandoRadicacionAsync(
            instance.Id, instance.TenantId, matrizPrecedencia: null, force: false, Ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().NotBe(rechazado.Id);
        result.Modo.Should().BeNull("no hay radicación vigente: generación normal");
        result.Regenerado.Should().BeTrue();
        instance.Attachments.Should().Contain(rechazado);
        _storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task R2_CarreraRechazo_TramiteRechazadoConSubmissionAunRegistrada_ElWorkerRegenera()
    {
        // ConsultarEstadoQuipuxHandler transiciona (commit + encola) ANTES de marcar la submission
        // `rechazado`: el worker puede leerla aún `registrado` (el lookup la devuelve como fija).
        var instance = Instancia(TramiteEstado.Rechazado);
        var radicado = Adjuntar(instance, "consolidado_maestro", subidoHace: TimeSpan.FromHours(1));
        instance.InvalidarConsolidados();
        Radicar(instance, radicado.Id);

        var resultado = await Worker().HandleAsync(instance.TenantId, instance.Id, TipoConsolidado.Maestro, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.Regenerado);
        await _lookup.DidNotReceive().AttachmentRadicadoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        instance.Attachments.Should().Contain(radicado, "sigue protegido por RetirarFilas");
        _storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task R2_Registrado_RadicadoFijoSigueFuncionando_EnWorkerYPostOt()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var radicado = Adjuntar(instance, "consolidado_maestro");
        instance.InvalidarConsolidados();
        Radicar(instance, radicado.Id);

        var worker = await Worker().HandleAsync(instance.TenantId, instance.Id, TipoConsolidado.Maestro, Ct);
        var (post, error) = await Maestro().HandleRespetandoRadicacionAsync(
            instance.Id, instance.TenantId, matrizPrecedencia: null, force: true, Ct);

        worker.Should().Be(ResultadoRegeneracionAnticipada.OmitidoMaestroRadicado);
        error.Should().BeNull();
        post!.Modo.Should().Be(ConsolidadoEntregaModos.RadicadoFijo);
        post.Document.AttachmentId.Should().Be(radicado.Id);
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public void R2_SubmissionPendiente_ProtegeSuAdjuntoFrenteARetirarFilas()
    {
        // AttachmentsProtegidosAsync incluye las pendientes aunque les falte RegisteredAt (N4/L-N1).
        var instance = Instancia(TramiteEstado.Preparado);
        var enviandose = Adjuntar(instance, "consolidado_maestro", subidoHace: TimeSpan.FromMinutes(5));
        var previos = ConsolidadoReemplazoSeguro.Previos(instance, "consolidado_maestro");

        var retirados = ConsolidadoReemplazoSeguro.RetirarFilas(instance, _repo, previos, new HashSet<Guid> { enviandose.Id });

        retirados.Should().BeEmpty();
        instance.Attachments.Should().Contain(enviandose);
        _repo.DidNotReceive().RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>());
    }

    // ── R4 — dos filas del maestro ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task R4_Worker_ConDosFilas_DecideLaMasReciente_NoLaPrimeraDeLaColeccion()
    {
        var instance = Instancia(TramiteEstado.Rechazado);
        var viejaUser = Adjuntar(instance, "consolidado_maestro", subidoHace: TimeSpan.FromHours(2));
        viejaUser.Source = "user";
        Adjuntar(instance, "consolidado_maestro"); // la más reciente, del sistema
        instance.ConsolidadoMaestroVigente = true;

        (await Worker().HandleAsync(instance.TenantId, instance.Id, TipoConsolidado.Maestro, Ct))
            .Should().Be(ResultadoRegeneracionAnticipada.OmitidoVigente, "la vigente es la más reciente (system)");

        var otra = Instancia(TramiteEstado.Rechazado);
        Adjuntar(otra, "consolidado_maestro", subidoHace: TimeSpan.FromHours(2));
        var nuevaUser = Adjuntar(otra, "consolidado_maestro");
        nuevaUser.Source = "user";

        (await Worker().HandleAsync(otra.TenantId, otra.Id, TipoConsolidado.Maestro, Ct))
            .Should().Be(ResultadoRegeneracionAnticipada.OmitidoCargadoManual);
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public void R4_ListadoOt_UnaFilaPorTipo_RadicadoFijoSiAplica()
    {
        var radicado = Dto("consolidado_maestro", TimeSpan.FromHours(3));
        var regenerado = Dto("consolidado_maestro", TimeSpan.Zero);
        var wizardViejo = Dto("consolidado", TimeSpan.FromHours(2));
        var wizardNuevo = Dto("consolidado", TimeSpan.FromHours(1));
        var factura = Dto("factura", TimeSpan.FromHours(5));
        var fur = Dto("fur", TimeSpan.FromHours(4));
        IReadOnlyList<AttachmentDto> docs = [factura, radicado, fur, wizardViejo, regenerado, wizardNuevo];

        var conRadicado = ConsolidadoListado.UnoPorTipo(docs, radicado.Id);
        var sinRadicado = ConsolidadoListado.UnoPorTipo(docs, radicadoId: null);

        conRadicado.Should().Equal(factura, radicado, fur, wizardNuevo);
        sinRadicado.Should().Equal(factura, fur, regenerado, wizardNuevo);
    }

    [Fact]
    public void R4_ListadoOt_RadicadoQueYaNoEstaEnLaLista_CaeAlMasReciente_YSinConsolidadosNoCambiaNada()
    {
        var viejo = Dto("consolidado_maestro", TimeSpan.FromHours(3));
        var nuevo = Dto("consolidado_maestro", TimeSpan.Zero);
        var factura = Dto("factura", TimeSpan.FromHours(5));

        ConsolidadoListado.UnoPorTipo([viejo, nuevo, factura], Guid.NewGuid()).Should().Equal(nuevo, factura);
        ConsolidadoListado.UnoPorTipo([factura], null).Should().Equal(factura);
        ConsolidadoListado.UnoPorTipo([], null).Should().BeEmpty();
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset Ahora = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static AttachmentDto Dto(string tipo, TimeSpan hace) =>
        new(Guid.NewGuid(), tipo, $"{tipo}.pdf", "application/pdf", 10, "sha", "system", Ahora - hace);

    private void Radicar(ProcedureInstance instance, Guid attachmentId)
    {
        _lookup.AttachmentRadicadoAsync(instance.TenantId, instance.Id, Arg.Any<CancellationToken>())
            .Returns((Guid?)attachmentId);
        Proteger(instance, attachmentId);
    }

    private void Proteger(ProcedureInstance instance, Guid attachmentId) =>
        _lookup.AttachmentsProtegidosAsync(instance.TenantId, instance.Id, Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { attachmentId });

    private ProcedureInstance Instancia(string estado)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-012760",
            Status = estado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Adjuntar(instance, "fur", subidoHace: TimeSpan.FromDays(1));
        Adjuntar(instance, "factura", subidoHace: TimeSpan.FromDays(1));
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private ProcedureInstanceAttachment Adjuntar(ProcedureInstance instance, string tipo, TimeSpan? subidoHace = null)
    {
        var path = $"{instance.Id:D}/{tipo}-{Guid.NewGuid():N}";
        _storage.Files[path] = System.Text.Encoding.UTF8.GetBytes($"%PDF-{tipo}");
        var adjunto = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = $"sha-{tipo}",
            StoragePath = path,
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow - (subidoHace ?? TimeSpan.Zero),
        };
        instance.Attachments.Add(adjunto);
        return adjunto;
    }

    private sealed class FakeMerger : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

        public byte[] Compose(MergeRequest request) => Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    private sealed class MemStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public List<string> Deleted { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = new();

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_saved_{Saved.Count}";
            Files[path] = ms.ToArray();
            Saved.Add(path);
            return new StoredFile(path, $"sha-{tipo}-nuevo", ms.Length);
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

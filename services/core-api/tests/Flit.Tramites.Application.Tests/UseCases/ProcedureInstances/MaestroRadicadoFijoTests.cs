using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Hallazgos de code-review/security de la Épica #12760 sobre el consolidado maestro:
/// <list type="bullet">
///   <item><b>F1 (HU #12787 AC2)</b> — el maestro radicado ante Quipux queda FIJO en el servidor: la
///   regeneración anticipada lo omite (<c>maestro_radicado</c>), la entrega lo sirve tal cual
///   (<c>modo=radicado_fijo</c>) aunque llegue <c>force</c>, el POST OT no lo regenera y el reemplazo
///   seguro nunca retira ni borra un adjunto radicado.</item>
///   <item><b>F2 (HU #12797)</b> — con transacción ambiente, los borrados del reemplazo esperan al
///   commit real (doble del scope: <see cref="IProcedureInstanceRepository.TryDeferUntilTransactionEnds"/>).</item>
///   <item><b>F4 (HU #12797)</b> — ante un conflicto de concurrencia con el worker, la entrega relee y
///   sirve el adjunto vigente, no el capturado (ya borrado).</item>
/// </list>
/// Generadores REALES sobre repositorio sustituto y storage en memoria.
/// <para>Uso de ejemplo:
/// <c>var (r, _) = await entrega.HandleAsync(new(id, tenant, ConsolidadoEntregaTipo.Maestro, Force: true));</c>
/// ⇒ <c>r.Modo == "radicado_fijo"</c> y <c>r.Document.AttachmentId</c> = el radicado.</para>
/// </summary>
public sealed class MaestroRadicadoFijoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IMaestroRadicadoLookup _lookup = Substitute.For<IMaestroRadicadoLookup>();
    private readonly MemStorage _storage = new();
    private readonly FakeMerger _merger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public MaestroRadicadoFijoTests()
    {
        _lookup.AttachmentsProtegidosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());
    }

    private GenerarConsolidadoMaestroHandler Maestro() =>
        new(_repo, _merger, _storage, maestroRadicado: _lookup);

    private EntregarConsolidadoHandler Entrega() =>
        new(_repo, new GenerarConsolidadoHandler(_repo, _merger, _storage), Maestro(), bitacora: null, maestroRadicado: _lookup);

    private RegenerarConsolidadoAnticipadoHandler Worker() =>
        new(_repo, new GenerarConsolidadoHandler(_repo, _merger, _storage), Maestro(), logger: null, bitacora: null, maestroRadicado: _lookup);

    // ── F1.2 — worker ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task F1_RadicacionQuipux_LuegoWorker_OmiteConMaestroRadicado_YElAdjuntoSigueExistiendo()
    {
        // Estado tras la radicación del canal Quipux: preparado → entregado invalidó las banderas.
        var instance = Instancia(TramiteEstado.Entregado);
        var radicado = Adjuntar(instance, "consolidado_maestro");
        instance.InvalidarConsolidados();
        Radicar(instance, radicado.Id);

        var resultado = await Worker().HandleAsync(instance.TenantId, instance.Id, TipoConsolidado.Maestro, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.OmitidoMaestroRadicado);
        MaestroRadicadoFijo.Motivo.Should().Be("maestro_radicado");
        instance.Attachments.Should().Contain(radicado, "la fila que referencia quipux_submissions sigue en pie");
        _storage.Files.Should().ContainKey(radicado.StoragePath, "su binario no se borró");
        _storage.Saved.Should().BeEmpty();
        _repo.DidNotReceive().RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task F1_Worker_SinRadicacion_RegeneraComoAntes()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        Adjuntar(instance, "consolidado_maestro");
        instance.InvalidarConsolidados();

        var resultado = await Worker().HandleAsync(instance.TenantId, instance.Id, TipoConsolidado.Maestro, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.Regenerado);
    }

    [Fact]
    public async Task F1_Worker_Wizard_NoConsultaLaRadicacion()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        Adjuntar(instance, "consolidado");
        instance.ConsolidadoWizardVigente = true;

        var resultado = await Worker().HandleAsync(instance.TenantId, instance.Id, TipoConsolidado.Wizard, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.OmitidoVigente);
        await _lookup.DidNotReceive().AttachmentRadicadoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── F1.3/F1.4 — entrega ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task F1_EntregaConForce_SobreRadicado_MismoAttachmentId_ModoRadicadoFijo()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var radicado = Adjuntar(instance, "consolidado_maestro");
        instance.ConsolidadoMaestroVigente = false;
        Radicar(instance, radicado.Id);

        var (result, error) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro, Force: true, SoloLectura: false), Ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().Be(radicado.Id);
        result.Modo.Should().Be(ConsolidadoEntregaModos.RadicadoFijo).And.Be("radicado_fijo");
        result.Regenerado.Should().BeFalse();
        result.DefinitivoPorEstadoFinal.Should().BeFalse();
        _storage.Saved.Should().BeEmpty("ni force ni la bandera abajo regeneran el maestro radicado");
        _storage.Deleted.Should().BeEmpty();
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task F1_Entrega_RadicadoConDatosRotos_SoloLectura_SinRegenerar()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var otroMaestro = Adjuntar(instance, "consolidado_maestro");
        instance.ConsolidadoMaestroVigente = false;
        Radicar(instance, Guid.NewGuid()); // la submission apunta a un adjunto que ya no existe

        var (result, error) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro, Force: true), Ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().Be(otroMaestro.Id);
        result.Modo.Should().Be(ConsolidadoEntregaModos.SoloLectura);
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task F1_Entrega_RadicadoConDatosRotos_SinMaestro_ConsolidadoNoGenerado()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        Radicar(instance, Guid.NewGuid());

        var (result, error) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro), Ct);

        result.Should().BeNull();
        error.Should().Be(EntregarConsolidadoHandler.ConsolidadoNoGenerado);
        _storage.Saved.Should().BeEmpty("datos rotos no autorizan a regenerar");
    }

    [Fact]
    public async Task F1_Entrega_EstadoFinalConRadicacion_SirveElRadicadoComoDefinitivo()
    {
        var instance = Instancia(TramiteEstado.Aprobado);
        var radicado = Adjuntar(instance, "consolidado_maestro", subidoHace: TimeSpan.FromHours(2));
        Adjuntar(instance, "consolidado_maestro"); // uno posterior (p. ej. regenerado antes del fix)
        Radicar(instance, radicado.Id);

        var (result, _) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro), Ct);

        result!.Document.AttachmentId.Should().Be(radicado.Id, "el organismo tuvo a la vista el radicado");
        result.DefinitivoPorEstadoFinal.Should().BeTrue();
        result.Modo.Should().Be(ConsolidadoEntregaModos.DefinitivoEstadoFinal);
    }

    // ── F1.7 — POST OT consolidado-maestro ──────────────────────────────────────────────────────

    [Fact]
    public async Task F1_PostOt_ConForce_SobreRadicado_NoRegenera()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var radicado = Adjuntar(instance, "consolidado_maestro");
        instance.ConsolidadoMaestroVigente = false;
        Radicar(instance, radicado.Id);

        var (result, error) = await Maestro().HandleRespetandoRadicacionAsync(
            instance.Id, instance.TenantId, matrizPrecedencia: null, force: true, Ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().Be(radicado.Id);
        result.Modo.Should().Be(ConsolidadoEntregaModos.RadicadoFijo);
        _storage.Saved.Should().BeEmpty();
        _storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task F1_PostOt_SinRadicacion_RegeneraConForceComoAntes()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var previo = Adjuntar(instance, "consolidado_maestro");
        instance.ConsolidadoMaestroVigente = true;

        var (result, error) = await Maestro().HandleRespetandoRadicacionAsync(
            instance.Id, instance.TenantId, matrizPrecedencia: null, force: true, Ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().NotBe(previo.Id);
        _storage.Saved.Should().ContainSingle();
    }

    // ── F1.6 — reemplazo seguro protege el radicado ─────────────────────────────────────────────

    [Fact]
    public void F1_RetirarFilas_NoTocaElAdjuntoRadicado()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var radicado = Adjuntar(instance, "consolidado_maestro", subidoHace: TimeSpan.FromHours(1));
        var otro = Adjuntar(instance, "consolidado_maestro");
        var previos = ConsolidadoReemplazoSeguro.Previos(instance, "consolidado_maestro");

        var retirados = ConsolidadoReemplazoSeguro.RetirarFilas(instance, _repo, previos, new HashSet<Guid> { radicado.Id });

        retirados.Should().Equal(otro);
        instance.Attachments.Should().Contain(radicado).And.NotContain(otro);
        _repo.Received(1).RemoveAttachment(otro);
        _repo.DidNotReceive().RemoveAttachment(radicado);
    }

    [Fact]
    public async Task F1_RegeneracionDelCanalQuipux_ConservaFilaYBinarioDelRadicado()
    {
        // HandleAsync (sin la regla de «fijo») es la vía del canal de radicación tras un rechazo: genera
        // uno nuevo, pero el radicado anterior no se retira ni se borra.
        var instance = Instancia(TramiteEstado.Preparado);
        var radicado = Adjuntar(instance, "consolidado_maestro", subidoHace: TimeSpan.FromHours(1));
        _lookup.AttachmentsProtegidosAsync(instance.TenantId, instance.Id, Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { radicado.Id });

        var (result, error) = await Maestro().HandleAsync(instance.Id, instance.TenantId, null, force: true, Ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().NotBe(radicado.Id);
        instance.Attachments.Should().Contain(radicado);
        _storage.Files.Should().ContainKey(radicado.StoragePath);
        _storage.Deleted.Should().BeEmpty();
        _repo.DidNotReceive().RemoveAttachment(radicado);
    }

    // ── F2 — transacción ambiente (doble del scope) ────────────────────────────────────────────

    [Fact]
    public async Task F2_ConTransaccionAmbiente_LosBorradosEsperanAlCommitReal()
    {
        var (instance, previo, stored, nuevo, previos) = PrepararReemplazo();
        Action? alConfirmar = null;
        Action? alRevertir = null;
        _repo.TryDeferUntilTransactionEnds(Arg.Do<Action>(a => alConfirmar = a), Arg.Do<Action?>(a => alRevertir = a))
            .Returns(true);

        await ConsolidadoReemplazoSeguro.ConfirmarAsync(instance, _repo, _storage, stored, nuevo, previos, null, null, Ct);

        _storage.Deleted.Should().BeEmpty("el SaveChanges dentro de la transacción ambiente no confirmó nada");
        alConfirmar.Should().NotBeNull();
        alConfirmar!();
        _storage.Deleted.Should().Equal(previo.StoragePath);
        alRevertir.Should().NotBeNull();
    }

    [Fact]
    public async Task F2_ConTransaccionAmbienteRevertida_SeBorraElNuevo_YElAnteriorQueda()
    {
        var (instance, previo, stored, nuevo, previos) = PrepararReemplazo();
        Action? alRevertir = null;
        _repo.TryDeferUntilTransactionEnds(Arg.Any<Action>(), Arg.Do<Action?>(a => alRevertir = a)).Returns(true);

        await ConsolidadoReemplazoSeguro.ConfirmarAsync(instance, _repo, _storage, stored, nuevo, previos, null, null, Ct);
        alRevertir!();

        _storage.Deleted.Should().Equal(stored.StoragePath);
        _storage.Files.Should().ContainKey(previo.StoragePath, "la BD revertida sigue apuntando al anterior");
    }

    [Fact]
    public async Task F2_SinTransaccionAmbiente_BorraDeInmediato()
    {
        var (instance, previo, stored, nuevo, previos) = PrepararReemplazo();
        _repo.TryDeferUntilTransactionEnds(Arg.Any<Action>(), Arg.Any<Action?>()).Returns(false);

        await ConsolidadoReemplazoSeguro.ConfirmarAsync(instance, _repo, _storage, stored, nuevo, previos, null, null, Ct);

        _storage.Deleted.Should().Equal(previo.StoragePath);
    }

    // ── F4 — carrera entrega diferida vs worker ─────────────────────────────────────────────────

    [Fact]
    public async Task F4_ConflictoDeConcurrencia_RecargaYSirveElVigenteActual_NoElCapturado()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var capturado = Adjuntar(instance, "consolidado_maestro");
        instance.ConsolidadoMaestroVigente = false;

        // Estado real tras el worker: M1 borrado, M2 vigente.
        var fresca = Instancia(TramiteEstado.Entregado, instance.Id, instance.TenantId);
        var vigente = Adjuntar(fresca, "consolidado_maestro");
        fresca.ConsolidadoMaestroVigente = true;
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
            .Returns(instance, fresca);

        var conflicto = new InvalidOperationException("row_version");
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(conflicto);
        _repo.IsConcurrencyConflict(conflicto).Returns(true);

        var (result, error) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro), Ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().Be(vigente.Id).And.NotBe(capturado.Id);
        result.AvisosCascada.Should().BeNullOrEmpty("no hubo fallo de regeneración: la ganó el worker");
        result.Modo.Should().Be(ConsolidadoEntregaModos.Vigente);
        _repo.Received(1).ResetTracking();
    }

    [Fact]
    public async Task F4_ExcepcionQueNoEsConflicto_SirveElAnteriorConAviso_ComoAntes()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var capturado = Adjuntar(instance, "consolidado_maestro");
        instance.ConsolidadoMaestroVigente = false;
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("disco"));

        var (result, _) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro), Ct);

        result!.Document.AttachmentId.Should().Be(capturado.Id);
        result.AvisosCascada.Should().ContainSingle();
        _repo.DidNotReceive().ResetTracking();
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────

    private (ProcedureInstance Instance, ProcedureInstanceAttachment Previo, StoredFile Stored,
        ProcedureInstanceAttachment Nuevo, IReadOnlyList<ProcedureInstanceAttachment> Previos) PrepararReemplazo()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var previo = Adjuntar(instance, "consolidado_maestro");
        var previos = ConsolidadoReemplazoSeguro.Previos(instance, "consolidado_maestro");
        var stored = new StoredFile($"{instance.Id:D}/consolidado_maestro_nuevo", "sha-nuevo", 5);
        _storage.Files[stored.StoragePath] = [1, 2, 3, 4, 5];
        previos = ConsolidadoReemplazoSeguro.RetirarFilas(instance, _repo, previos);
        var nuevo = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = "consolidado_maestro",
            Filename = "nuevo.pdf",
            Mimetype = "application/pdf",
            StoragePath = stored.StoragePath,
            Sha256 = stored.Sha256,
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow,
        };
        instance.Attachments.Add(nuevo);
        return (instance, previo, stored, nuevo, previos);
    }

    private void Radicar(ProcedureInstance instance, Guid attachmentId)
    {
        _lookup.AttachmentRadicadoAsync(instance.TenantId, instance.Id, Arg.Any<CancellationToken>())
            .Returns((Guid?)attachmentId);
        _lookup.AttachmentsProtegidosAsync(instance.TenantId, instance.Id, Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { attachmentId });
    }

    private ProcedureInstance Instancia(string estado, Guid? id = null, Guid? tenantId = null)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-012787",
            Status = estado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Adjuntar(instance, "fur");
        Adjuntar(instance, "factura");
        if (id is null)
        {
            _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
            _repo.GetByIdWithChecklistGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        }

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

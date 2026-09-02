using System.Text.Json.Nodes;
using System.Text;
using Flit.Tramites.Application.Ocr;
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

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status) =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000300",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>
    /// PDF con cabecera real: <see cref="LtPdf"/> lleva texto suelto y el analizador lo descarta por
    /// magic bytes antes de mirarlo, así que no sirve para ejercer el camino de análisis.
    /// </summary>
    private static UploadAttachmentInput LtPdfConCabecera() =>
        new(AdjuntarLicenciaTransitoHandler.Tipo, "lt.pdf", "application/pdf", 512,
            new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7\nlicencia-transito")));

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

        var (result, error, _) = await _adjuntar.HandleAsync(id, tenantId, LtPdf(), null, null, ct);

        error.Should().BeNull();
        result!.Tipo.Should().Be("licencia_transito");
        result.Source.Should().Be("ot");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "licencia_transito");
        instance.Events.Should().ContainSingle(e => e.Tipo == "lt_adjuntada");
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task AdjuntarLt_InvalidaLaMarcaDeConsolidadoMaestro()
    {
        // Feature #10701 — adjuntar la LT cambia el expediente: baja la marca de vigencia a false
        // para que el próximo "Ver consolidado" regenere el PDF incluyéndola.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instance(id, tenantId, TramiteEstado.Aprobado);
        instance.ConsolidadoMaestroVigente = true;
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(instance);

        var (_, error, _) = await _adjuntar.HandleAsync(id, tenantId, LtPdf(), null, null, ct);

        error.Should().BeNull();
        instance.ConsolidadoMaestroVigente.Should().BeFalse();
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

        var (result, error, _) = await _adjuntar.HandleAsync(id, tenantId, LtPdf(), null, null, ct);

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

        var (result, error, _) = await _adjuntar.HandleAsync(id, tenantId, LtPdf("lt_nueva.pdf"), null, null, ct);

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

        var (result, error, _) = await _adjuntar.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), input, null, null, ct);

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

        var (_, error, _) = await _adjuntar.HandleAsync(id, tenantId, LtPdf(), user, null, ct);

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

    [Fact]
    public async Task DescargarConsolidado_SoloMaestro_LoDevuelve()
    {
        // F2 (Feature #10701): "Ver consolidado" debe mostrar el maestro cuando es lo único
        // que el OT generó (antes solo consideraba el tipo "consolidado").
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instance(id, tenantId, TramiteEstado.Aprobado);
        _storage.Contents["path/maestro"] = Encoding.UTF8.GetBytes("pdf-maestro");
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            Tipo = "consolidado_maestro",
            Filename = "consolidado_maestro_TRM.pdf",
            Mimetype = "application/pdf",
            StoragePath = "path/maestro",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(instance);

        var (result, error) = await _descargar.HandleAsync(id, tenantId, ct);

        error.Should().BeNull();
        result!.Filename.Should().Be("consolidado_maestro_TRM.pdf");
    }

    [Fact]
    public async Task DescargarConsolidado_MaestroMasReciente_GanaSobreConsolidado()
    {
        // Entre {consolidado, consolidado_maestro} devuelve el más reciente por UploadedAt.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instance(id, tenantId, TramiteEstado.Aprobado);
        _storage.Contents["path/std"] = Encoding.UTF8.GetBytes("pdf-std");
        _storage.Contents["path/maestro"] = Encoding.UTF8.GetBytes("pdf-maestro");
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            Tipo = "consolidado",
            Filename = "consolidado_std.pdf",
            Mimetype = "application/pdf",
            StoragePath = "path/std",
            UploadedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            Tipo = "consolidado_maestro",
            Filename = "consolidado_maestro.pdf",
            Mimetype = "application/pdf",
            StoragePath = "path/maestro",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(instance);

        var (result, error) = await _descargar.HandleAsync(id, tenantId, ct);

        error.Should().BeNull();
        result!.Filename.Should().Be("consolidado_maestro.pdf");
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
        await AddPdf(instance, "factura");
        await AddPdf(instance, "aduana");
        await AddPdf(instance, "impronta");
        await AddPdf(instance, "fur");
        await AddPdf(instance, "licencia_transito");
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, ct).Returns(instance);

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

        public byte[] Compose(Flit.Tramites.Application.Documents.MergeRequest request) =>
            Merge(request.Parts.Select(p => p.Pdf).ToList());
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

    // ── HU #11996 — verificación por OCR de la LT que entrega el OT ──────────

    /// <summary>Analizador que siempre falla, para probar que el adjunto NO depende del OCR.</summary>
    private sealed class AnalizadorCaido : IDocumentOcrAnalyzer
    {
        public Task<DocumentOcrAnalysis> AnalyzeAsync(string tipo, ReadOnlyMemory<byte> content, string mediaType, CancellationToken ct)
            => throw new HttpRequestException("proveedor caido");
    }

    [Fact]
    public async Task Lt_se_adjunta_igual_aunque_el_proveedor_de_ia_este_caido()
    {
        // La LT es el entregable del OT: perderla porque la IA no responde sería peor que no
        // verificarla. El adjunto se crea y el resultado del análisis viaja en null.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instance(id, tenantId, TramiteEstado.Entregado);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(instance);
        var handler = new AdjuntarLicenciaTransitoHandler(
            _repo, _storage, new AnalyzeDocumentHandler(new AnalizadorCaido()));

        var (result, error, ocr) = await handler.HandleAsync(id, tenantId, LtPdf(), null, null, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        ocr.Should().BeNull("un fallo del proveedor no puede costarle al OT el adjunto");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == AdjuntarLicenciaTransitoHandler.Tipo);
    }

    [Fact]
    public async Task Lt_se_adjunta_igual_cuando_no_hay_analizador_configurado()
    {
        // Entornos sin OCR (mock apagado, sin key): el handler debe seguir funcionando igual que antes.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instance(id, tenantId, TramiteEstado.Entregado);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(instance);
        var handler = new AdjuntarLicenciaTransitoHandler(_repo, _storage);

        var (result, error, ocr) = await handler.HandleAsync(id, tenantId, LtPdf(), null, null, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        ocr.Should().BeNull();
        instance.Attachments.Should().ContainSingle(a => a.Tipo == AdjuntarLicenciaTransitoHandler.Tipo);
    }

    [Fact]
    public void El_ocr_de_la_lt_usa_el_prompt_de_la_licencia_de_transito()
    {
        // El tipo documental del OT (`licencia_transito`) y el de la casilla del wizard
        // (`tarjeta_propiedad`) son códigos distintos para el MISMO documento: comparten prompt.
        AdjuntarLicenciaTransitoHandler.TipoOcr.Should().Be("tarjeta_propiedad");
        DocumentOcrPrompts.IsSupported(AdjuntarLicenciaTransitoHandler.TipoOcr).Should().BeTrue();
        AdjuntarLicenciaTransitoHandler.Tipo.Should().Be("licencia_transito");
    }

    // ── HU #12042 — el análisis que el OT ya vio ────────────────────────────────────────────

    /// <summary>Analizador que cuenta cuántas veces lo llaman: si lo llaman, se pagó dos veces.</summary>
    private sealed class AnalizadorEspia : IDocumentOcrAnalyzer
    {
        public int Llamadas { get; private set; }

        public Task<DocumentOcrAnalysis> AnalyzeAsync(string tipo, ReadOnlyMemory<byte> content, string mediaType, CancellationToken ct)
        {
            Llamadas++;
            return Task.FromResult(new DocumentOcrAnalysis(true, new JsonObject { ["vehiculo_vin"] = "OTRO_VIN" }));
        }
    }

    [Fact]
    public async Task Con_analisis_precomputado_no_vuelve_a_analizar_y_registra_lo_que_vio_el_OT()
    {
        // El frontend analiza al SELECCIONAR el archivo para enseñarle el veredicto al OT antes de que
        // decida, y manda ese resultado. Volver a analizar aquí no solo costaría el doble: dos lecturas
        // del mismo documento pueden diferir —en la prueba en vivo dieron VIN distintos—, y registrar
        // una cosa mientras se le mostró otra al usuario sería peor que no mostrarle nada.
        var (id, tenantId, ct) = (Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(Instance(id, tenantId, TramiteEstado.Aprobado));
        var espia = new AnalizadorEspia();
        var handler = new AdjuntarLicenciaTransitoHandler(_repo, _storage, new AnalyzeDocumentHandler(espia));
        var yaVisto = new JsonObject { ["es_valido"] = true, ["vehiculo_vin"] = "VIN_QUE_VIO_EL_OT" };

        var (result, error, ocr) = await handler.HandleAsync(id, tenantId, LtPdf(), null, yaVisto, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        espia.Llamadas.Should().Be(0, "el documento ya venía analizado");
        ocr!.Data!["vehiculo_vin"]!.GetValue<string>().Should().Be("VIN_QUE_VIO_EL_OT");
    }

    [Fact]
    public async Task Sin_analisis_precomputado_el_backend_analiza_por_su_cuenta()
    {
        // Compatibilidad: si el frontend no pudo analizar (proveedor caído al seleccionar, archivo
        // grande) o llama otro cliente, el backend sigue haciendo su parte.
        var (id, tenantId, ct) = (Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken);
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).Returns(Instance(id, tenantId, TramiteEstado.Aprobado));
        var espia = new AnalizadorEspia();
        var handler = new AdjuntarLicenciaTransitoHandler(_repo, _storage, new AnalyzeDocumentHandler(espia));

        var (_, error, ocr) = await handler.HandleAsync(id, tenantId, LtPdfConCabecera(), null, null, ct);

        error.Should().BeNull();
        espia.Llamadas.Should().Be(1);
        ocr!.Data!["vehiculo_vin"]!.GetValue<string>().Should().Be("OTRO_VIN");
    }
}

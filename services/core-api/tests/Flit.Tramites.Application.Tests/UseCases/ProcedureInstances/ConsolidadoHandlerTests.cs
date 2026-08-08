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
using Flit.Tramites.Domain.Tramites.Estados;

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

        // La portada la ejercita el merger real (HU #10857); aquí solo concatenamos las partes en
        // orden para preservar las aserciones de prelación.
        public byte[] Compose(MergeRequest request) =>
            Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    private sealed class FakeRegenerator : IExpedienteHotDocumentsRegenerator
    {
        public int Calls { get; private set; }

        public Task<string?> RegenerateHotDocumentsAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<string?>(null);
        }
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
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(
                string.IsNullOrWhiteSpace(storagePath)
                    ? null
                    : ($"https://s3.test/view/{Uri.EscapeDataString(storagePath)}", DateTimeOffset.UtcNow.AddMinutes(10)));
    }

    private static ProcedureInstance MatriculaInstance(Guid id, Guid tenantId)
    {
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000099",
            Status = TramiteEstado.Borrador,
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
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "traspaso",
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoTraspasoStandard,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Obligatorios del checklist de traspaso + FUR + certificados (comprador y vendedor) + compraventa.
        AddAttachment(instance, "fur", "fur.pdf", "%PDF-fur");
        AddAttachment(instance, "certificado_identidad", "cert.pdf", "%PDF-cert");
        AddAttachment(instance, "certificado_identidad_vendedor", "cert_vend.pdf", "%PDF-cert-vend");
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
            Source = tipo is "fur" or "certificado_identidad" or "certificado_identidad_vendedor" ? "system" : "user",
            UploadedAt = DateTimeOffset.UtcNow,
        });
    }

    private string ConsolidadoContent()
    {
        var path = _storage.Saved.Last();
        return System.Text.Encoding.UTF8.GetString(_storage.Files[path]);
    }

    [Fact]
    public async Task HandleAsync_Migrado_NoRegeneraYRetornaSoloLectura()
    {
        // Migración V1→V2: el trámite migrado ya trae su consolidado histórico (source=migration).
        // Regenerarlo lo reemplazaría; el handler sale con 'migrado_solo_lectura' sin escribir.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);
        instance.IsMigrated = true;
        instance.Status = TramiteEstado.Aprobado;
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().Be("migrado_solo_lectura");
        result.Should().BeNull();
        _storage.Saved.Should().BeEmpty(); // no generó/sobrescribió el consolidado
    }

    /// <summary>Prelación que el OT dejó configurada para el tipo de trámite (HU #11184).</summary>
    private sealed class FakeOtOrderProvider(params string[] codigos) : IOtConfiguredDocumentOrderProvider
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> GetConfiguredOrderAsync(
            Guid procedureTypeId, Guid? transitOfficeId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>(codigos);
        }
    }

    private sealed class FailingOtOrderProvider : IOtConfiguredDocumentOrderProvider
    {
        public Task<IReadOnlyList<string>> GetConfiguredOrderAsync(
            Guid procedureTypeId, Guid? transitOfficeId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("prelación no disponible");
    }

    [Fact]
    public async Task HU11184_AC1_AC2_ConPrelacionConfigurada_ElExpedienteSaleEnEseOrden_YElFurNoSeFuerzaAlPrincipio()
    {
        // El organismo bajó el FUR: primero compraventa y SOAT. Con la cabecera fija que anteponía
        // los generados esto era imposible — el FUR salía siempre en la primera página.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var provider = new FakeOtOrderProvider("compraventa", "soat", "fur", "certificado_identidad");
        var handler = new GenerarConsolidadoHandler(_repo, _merger, _storage, null, null, null, provider);

        var (_, error) = await handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        provider.Calls.Should().Be(1);
        var posiciones = PosicionesEnConsolidado("compraventa.pdf", "soat.pdf", "fur.pdf", "cert.pdf");
        posiciones.Should().NotContain(-1);
        posiciones.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task HU11184_AC3_SinPrelacionConfigurada_ConservaElOrdenDeSuModalidad()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var handler = new GenerarConsolidadoHandler(
            _repo, _merger, _storage, null, null, null, new FakeOtOrderProvider());

        var (_, error) = await handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        var posiciones = PosicionesEnConsolidado("fur.pdf", "cert.pdf", "compraventa.pdf", "soat.pdf");
        posiciones.Should().NotContain(-1);
        posiciones.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task HU11184_AC3_SiFallaLaConsultaDePrelacion_ElExpedienteSeGeneraIgual()
    {
        // Un fallo leyendo la configuración del OT no puede dejar al gestor sin expediente.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var handler = new GenerarConsolidadoHandler(
            _repo, _merger, _storage, null, null, null, new FailingOtOrderProvider());

        var (result, error) = await handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        var posiciones = PosicionesEnConsolidado("fur.pdf", "compraventa.pdf");
        posiciones.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task HU11184_AC4_ConPrelacionConfigurada_NoIncluyeConsolidadosPrevios()
    {
        // La regla "ningún expediente dentro de otro" tiene que sobrevivir al camino nuevo: olvidar
        // `consolidado_maestro` fue lo que duplicaba el expediente entero al aprobar el OT.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);
        AddAttachment(instance, "consolidado_maestro", "maestro.pdf", "%PDF-maestro");
        AddAttachment(instance, "biometric_selfie", "biometric.pdf", "%PDF-biometric");
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var provider = new FakeOtOrderProvider("fur", "compraventa", "consolidado_maestro");
        var handler = new GenerarConsolidadoHandler(_repo, _merger, _storage, null, null, null, provider);

        var (_, error) = await handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        var content = ConsolidadoContent();
        content.Should().NotContain("maestro.pdf");
        content.Should().NotContain("biometric.pdf");
        content.Should().Contain("fur.pdf");
    }

    /// <summary>
    /// Índice de cada documento dentro del consolidado, por su nombre de archivo (las pruebas
    /// guardan el filename como contenido, así que el PDF fusionado es su concatenación).
    /// </summary>
    private IReadOnlyList<int> PosicionesEnConsolidado(params string[] filenames)
    {
        var content = ConsolidadoContent();
        return [.. filenames.Select(f => content.IndexOf(f, StringComparison.Ordinal))];
    }

    [Fact]
    public async Task HU11183_AC3_TraspasoSinPrelacionConfigurada_ConservaElOrdenDeHoy()
    {
        // Golden test del orden vigente de traspaso: la HU #11174 hace configurable la prelación,
        // pero un OT que no ha configurado nada debe seguir recibiendo EXACTAMENTE este expediente.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (_, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        // FUR, certificados, compraventa e impronta por prelación; los que la lista no nombra
        // (rtm, paz y salvo, cédulas) al final por fecha de carga.
        var posiciones = PosicionesEnConsolidado(
            "fur.pdf", "cert.pdf", "cert_vend.pdf", "compraventa.pdf", "impronta.pdf",
            "soat.pdf", "rtm.pdf", "paz_salvo.pdf", "cedulas.pdf");
        posiciones.Should().NotContain(-1);
        posiciones.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task HU11183_AC4_MatriculaSinPrelacionConfigurada_ConservaElOrdenDeHoy()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (_, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        var posiciones = PosicionesEnConsolidado(
            "fur.pdf", "cert.pdf", "factura.pdf", "aduana.pdf", "impronta.pdf");
        posiciones.Should().NotContain(-1);
        posiciones.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task HandleAsync_Matricula_OrdenaPorModalidad()
    {
        // Matrícula ⇒ factura antes que aduana (precedencia por modalidad, que también ordena
        // los documentos generados —FUR/licencia— junto a los subidos).
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var handler = new GenerarConsolidadoHandler(_repo, _merger, _storage);

        var (result, error) = await handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        var content = ConsolidadoContent();
        content.IndexOf("factura", StringComparison.Ordinal)
            .Should().BeLessThan(content.IndexOf("aduana", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleAsync_ModalidadDistinta_GeneraConOrdenGenerico()
    {
        // HU #10522 (RF27/41): una modalidad distinta de matrícula/traspaso YA NO se rechaza;
        // se genera el consolidado con el orden genérico (documentos generados primero).
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.ModalidadEntrada = "sucesion";
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().NotBe("modalidad_no_soportada");
        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Document.Tipo.Should().Be("consolidado");
    }

    [Fact]
    public async Task HandleAsync_TraspasoSinFur_ReturnsFurRequerido()
    {
        // AC2 (#10455): traspaso sin FUR → fur_requerido, NUNCA modalidad_no_soportada.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "fur"));

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>())
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

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Document.Tipo.Should().Be("consolidado");
        instance.Events.Should().ContainSingle(e => e.Tipo == "consolidado_generado")
            .Which.Payload.Should().Contain("compraventa");
    }

    [Fact]
    public async Task HandleAsync_Traspaso_ElCertificadoSoatRtmVaEntreLosCertificadosYNoAlFinal()
    {
        // HU #11307 — `certificado_soat_rtm` y `certificado_rues_vendedor` no estaban en la lista de
        // prelación: caían al final por defecto (rank = Precedence.Length + 1), mezclados con "otro" y
        // ordenados solo por fecha de carga. El expediente que ve el organismo de tránsito los
        // presentaba en un sitio arbitrario que además podía cambiar entre regeneraciones.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);
        AddAttachment(instance, "certificado_soat_rtm", "soat_rtm_cert.pdf", "%PDF-soatrtmcert");
        AddAttachment(instance, "otro", "otro.pdf", "%PDF-otro");

        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (_, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        var content = ConsolidadoContent();
        var idxIdentidad = content.IndexOf("cert.pdf", StringComparison.Ordinal);
        var idxSoatRtm = content.IndexOf("soat_rtm_cert.pdf", StringComparison.Ordinal);
        var idxCompraventa = content.IndexOf("compraventa.pdf", StringComparison.Ordinal);
        var idxOtro = content.IndexOf("otro.pdf", StringComparison.Ordinal);

        idxSoatRtm.Should().BeGreaterThan(idxIdentidad, "va con los certificados generados");
        idxSoatRtm.Should().BeLessThan(idxCompraventa, "y antes de los documentos del checklist");
        idxSoatRtm.Should().BeLessThan(idxOtro, "ya no cae al final junto a los no clasificados");
    }

    [Fact]
    public async Task HandleAsync_Traspaso_IncluyeAmbosCertificadosEnOrden()
    {
        // El certificado de identidad del vendedor se fusiona tras el del comprador y antes de la
        // compraventa (prelación de TraspasoConsolidadoOrdering).
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);

        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        var content = ConsolidadoContent();
        var idxComprador = content.IndexOf("cert.pdf", StringComparison.Ordinal);
        var idxVendedor = content.IndexOf("cert_vend.pdf", StringComparison.Ordinal);
        var idxCompraventa = content.IndexOf("compraventa.pdf", StringComparison.Ordinal);
        idxComprador.Should().BeGreaterThanOrEqualTo(0);
        idxVendedor.Should().BeGreaterThanOrEqualTo(0);
        idxComprador.Should().BeLessThan(idxVendedor);
        idxVendedor.Should().BeLessThan(idxCompraventa);
    }

    [Fact]
    public async Task HandleAsync_SinFur_ReturnsFurRequerido()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "fur"));

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        result.Should().BeNull();
        error.Should().Be(SubmitGate.FurRequerido);
    }

    [Fact]
    public async Task HandleAsync_WizardVigente_DevuelveCacheadoSinRegenerar()
    {
        // HU #10860 (ADR-0032): con el expediente del wizard vigente y el consolidado presente, se
        // sirve el PDF cacheado (Regenerado=false) sin regenerar ni persistir.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.ConsolidadoWizardVigente = true;
        AddAttachment(instance, "consolidado", "consolidado.pdf", "%PDF-cons");
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        result!.Regenerado.Should().BeFalse();
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ForceTrue_InvalidaCacheYRegenera()
    {
        // Feature #11066 — force=true salta la caché del wizard, invalida flags y reconstruye el PDF.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.ConsolidadoWizardVigente = true;
        AddAttachment(instance, "consolidado", "consolidado.pdf", "%PDF-cons");
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, userId: null, force: true, CancellationToken.None);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        instance.ConsolidadoWizardVigente.Should().BeTrue();
        _storage.Saved.Should().NotBeEmpty();
        await _repo.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WizardInvalidado_ConFurExistente_NoRegeneraHotDocs()
    {
        // Feature #11066 — con FUR ya generado, el consolidado solo fusiona; NO vuelve a generar
        // el paquete en caliente (certificados, mandato, etc.).
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId); // ConsolidadoWizardVigente = false; tiene FUR
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var regenerator = new FakeRegenerator();
        var handler = new GenerarConsolidadoHandler(_repo, _merger, _storage, null, regenerator);

        var (result, error) = await handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        regenerator.Calls.Should().Be(0);
        result!.Regenerado.Should().BeTrue();
        instance.ConsolidadoWizardVigente.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ForceTrue_ConDocsExistentes_NoRegeneraHotDocs()
    {
        // Re-generar consolidado: invalida caché y reconstruye el PDF; no regenera documentos previos.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.ConsolidadoWizardVigente = true;
        AddAttachment(instance, "consolidado", "consolidado.pdf", "%PDF-cons");
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var regenerator = new FakeRegenerator();
        var handler = new GenerarConsolidadoHandler(_repo, _merger, _storage, null, regenerator);

        var (result, error) = await handler.HandleAsync(id, tenantId, userId: null, force: true, CancellationToken.None);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        regenerator.Calls.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_MatriculaCompleta_PersisteConsolidado()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);

        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Document.Tipo.Should().Be("consolidado");
        _storage.Saved.Should().ContainSingle();
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── HU #11017 — cascada: el consolidado genera lo que falte en vez de bloquear ──────────────

    /// <summary>Regenerador que "produce" el FUR faltante (lo añade a la instancia que verá el repo).</summary>
    private sealed class FakeFurProducer(ProcedureInstance instance, FakeStorage storage)
        : IExpedienteHotDocumentsRegenerator
    {
        public int Calls { get; private set; }

        public Task<string?> RegenerateHotDocumentsAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        {
            Calls++;
            var path = $"{instance.Id:D}/fur";
            storage.Files[path] = System.Text.Encoding.UTF8.GetBytes("%PDF-fur");
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                ProcedureInstanceId = instance.Id,
                TenantId = instance.TenantId,
                Tipo = "fur",
                Filename = "fur.pdf",
                Mimetype = "application/pdf",
                SizeBytes = 10,
                Sha256 = "sha-fur",
                StoragePath = path,
                Source = "system",
                UploadedAt = DateTimeOffset.UtcNow,
            });
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>Regenerador que falla con un motivo real del generador (p. ej. falta el organismo).</summary>
    private sealed class FakeFailingRegenerator(string error) : IExpedienteHotDocumentsRegenerator
    {
        public Task<string?> RegenerateHotDocumentsAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>(error);
    }

    private sealed class FakeImprontaGenerator : IImprontaAutoGenerator
    {
        public int Calls { get; private set; }

        public Task<string?> TryGenerateAsync(Guid id, Guid tenantId, Guid userId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<string?>(null);
        }
    }

    [Fact]
    public async Task HandleAsync_SinFur_LoGeneraEnCascadaYConsolida()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "fur"));
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);

        var regenerador = new FakeFurProducer(instance, _storage);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var handler = new GenerarConsolidadoHandler(_repo, _merger, _storage, null, regenerador);

        var (result, error) = await handler.HandleAsync(id, tenantId, CancellationToken.None);

        // Antes devolvía fur_requerido y el gestor tenía que generar el FUR a mano primero.
        error.Should().BeNull();
        result.Should().NotBeNull();
        regenerador.Calls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HandleAsync_SinFur_YRegeneracionFallida_DevuelveElMotivoReal()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "fur"));

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var handler = new GenerarConsolidadoHandler(
            _repo, _merger, _storage, null, new FakeFailingRegenerator("organismo_requerido"));

        var (result, error) = await handler.HandleAsync(id, tenantId, CancellationToken.None);

        result.Should().BeNull();
        // El gestor ve la causa REAL, no un genérico "falta el FUR" que no le dice qué hacer.
        error.Should().Be("organismo_requerido");
    }

    [Fact]
    public async Task HandleAsync_SinImpronta_LaGeneraCuandoHayUsuario()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "impronta"));
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var impronta = new FakeImprontaGenerator();
        var handler = new GenerarConsolidadoHandler(_repo, _merger, _storage, null, null, impronta);

        var (_, error) = await handler.HandleAsync(id, tenantId, Guid.NewGuid(), CancellationToken.None);

        error.Should().BeNull();
        impronta.Calls.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_SinUsuario_NoIntentaGenerarLaImpronta()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var impronta = new FakeImprontaGenerator();
        var handler = new GenerarConsolidadoHandler(_repo, _merger, _storage, null, null, impronta);

        await handler.HandleAsync(id, tenantId, CancellationToken.None);

        // El proveedor exige el operador: sin usuario no se intenta (y el consolidado igual sale).
        impronta.Calls.Should().Be(0);
    }

    // ── HU #11035 — el consolidado NO se duplica al regenerar ───────────────────────────────────

    [Fact]
    public async Task HandleAsync_RegenerarDosVeces_DejaUnUnicoConsolidado()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (primero, error1) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);
        error1.Should().BeNull();

        // Segunda generación: el expediente vuelve a invalidarse (lo que hace cualquier cambio del
        // trámite) y se regenera. El adjunto previo debe REEMPLAZARSE, no acumularse.
        instance.ConsolidadoWizardVigente = false;
        var (segundo, error2) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);
        error2.Should().BeNull();

        instance.Attachments.Count(a => string.Equals(a.Tipo, "consolidado", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        segundo!.Document.AttachmentId.Should().NotBe(primero!.Document.AttachmentId);
    }

    [Fact]
    public async Task HandleAsync_ConVariosConsolidadosPrevios_LosReemplazaTodos()
    {
        // Defensa ante datos ya duplicados (p. ej. por un doble clic anterior): la regeneración deja
        // el expediente con UN solo consolidado, no con los viejos más el nuevo.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        AddAttachment(instance, "consolidado", "consolidado-1.pdf", "%PDF-1");
        AddAttachment(instance, "consolidado", "consolidado-2.pdf", "%PDF-2");
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        var consolidados = instance.Attachments
            .Where(a => string.Equals(a.Tipo, "consolidado", StringComparison.OrdinalIgnoreCase))
            .ToList();
        consolidados.Should().ContainSingle();
        consolidados[0].Id.Should().Be(result!.Document.AttachmentId);
    }

    // ── Feature #11066 — consolidado NO regenera docs en caliente; solo fusiona (FUR si falta) ──

    private sealed class FailingRegenerator(string error) : IExpedienteHotDocumentsRegenerator
    {
        public int Calls { get; private set; }
        public Task<string?> RegenerateHotDocumentsAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<string?>(error);
        }
    }

    private sealed class FailingImprontaGenerator(string error) : IImprontaAutoGenerator
    {
        public int Calls { get; private set; }
        public Task<string?> TryGenerateAsync(Guid id, Guid tenantId, Guid userId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<string?>(error);
        }
    }

    [Fact]
    public async Task HandleAsync_ExpedienteInvalidado_NoRegeneraHotDocs_SoloFusiona()
    {
        // Feature #11066: con FUR ya persistido, invalidar el consolidado NO dispara regeneración
        // en caliente (eso es de Preparar). Solo fusiona el expediente existente.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.ConsolidadoWizardVigente = false;
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var regenerator = new FailingRegenerator("organismo_requerido");
        var handler = new GenerarConsolidadoHandler(
            _repo, _merger, _storage, null, regenerator);

        var (result, error) = await handler.HandleAsync(id, tenantId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
        regenerator.Calls.Should().Be(0);
        result!.AvisosCascada.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ImprontaFalla_ConsolidaSinAvisosCascada()
    {
        // Feature #11066: impronta best-effort; su fallo no bloquea ni publica avisos de cascada develop.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "impronta"));
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var impronta = new FailingImprontaGenerator("provider_unavailable");
        var handler = new GenerarConsolidadoHandler(
            _repo, _merger, _storage, null, null, impronta);

        var (result, error) = await handler.HandleAsync(
            id, tenantId, Guid.NewGuid(), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        impronta.Calls.Should().Be(1);
        result!.AvisosCascada.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_SinFallos_NoDevuelveAvisos()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        instance.ConsolidadoWizardVigente = false;
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var handler = new GenerarConsolidadoHandler(_repo, _merger, _storage, null, new FakeRegenerator());

        var (result, error) = await handler.HandleAsync(id, tenantId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.AvisosCascada.Should().BeNull();
    }

    // -- Ajuste del PO: el consolidado maestro NO puede entrar al consolidado del wizard -----------
    //
    // Al aprobar el organismo de transito se genera el `consolidado_maestro` (que YA contiene todos
    // los documentos) y se invalida el consolidado del wizard. Como el orden solo excluia el tipo
    // `consolidado`, la siguiente regeneracion mezclaba el maestro como un adjunto mas y cada
    // documento del expediente salia DOS VECES.

    [Fact]
    public async Task Traspaso_ConConsolidadoMaestro_NoLoIncluyeYNoDuplicaDocumentos()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId);

        // Estado tras la aprobacion del OT: existe el maestro (con TODO el expediente dentro) y el
        // consolidado del wizard previo.
        AddAttachment(instance, "consolidado_maestro", "maestro.pdf", "%PDF-maestro");
        AddAttachment(instance, "consolidado", "consolidado-previo.pdf", "%PDF-previo");
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var handler = new GenerarConsolidadoHandler(_repo, _merger, _storage, null, new FakeRegenerator());

        var (result, error) = await handler.HandleAsync(id, tenantId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();

        // El PDF resultante (el fake concatena los bytes de las partes, y cada parte es su filename) no
        // lleva ni el maestro ni el consolidado previo, y cada documento aparece UNA sola vez.
        var generado = ConsolidadoContent();
        generado.Should().NotContain("maestro.pdf");
        generado.Should().NotContain("consolidado-previo.pdf");
        Ocurrencias(generado, "fur.pdf").Should().Be(1);
        Ocurrencias(generado, "compraventa.pdf").Should().Be(1);
        Ocurrencias(generado, "soat.pdf").Should().Be(1);
    }

    [Fact]
    public async Task Matricula_ConConsolidadoMaestro_TampocoLoIncluye()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = MatriculaInstance(id, tenantId);
        AddAttachment(instance, "consolidado_maestro", "maestro.pdf", "%PDF-maestro");
        foreach (var att in instance.Attachments)
            _storage.Files[att.StoragePath] = System.Text.Encoding.UTF8.GetBytes(att.Filename);

        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var handler = new GenerarConsolidadoHandler(_repo, _merger, _storage, null, new FakeRegenerator());

        var (_, error) = await handler.HandleAsync(id, tenantId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        var generado = ConsolidadoContent();
        generado.Should().NotContain("maestro.pdf");
        Ocurrencias(generado, "fur.pdf").Should().Be(1);
    }

    private static int Ocurrencias(string texto, string aguja)
    {
        var total = 0;
        var desde = 0;
        while (true)
        {
            var i = texto.IndexOf(aguja, desde, StringComparison.Ordinal);
            if (i < 0)
                return total;
            total++;
            desde = i + aguja.Length;
        }
    }
}

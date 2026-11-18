using System.Text;
using System.Text.Json;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #14604 — «Cargar documento» del admin FLIT en cualquier estado: tipos habilitados por la matriz del tipo de
/// trámite, observación obligatoria, el vigente anterior queda histórico (no se borra), evento propio en la bitácora y
/// consumidores que ignoran las versiones anteriores.
/// </summary>
public sealed class AdminCargarDocumentoBug14604Tests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IResolvedChecklistMatrixProvider _matrix = Substitute.For<IResolvedChecklistMatrixProvider>();
    private readonly FakeStorage _storage = new();
    private readonly IDocumentTypeCatalog _catalog = Substitute.For<IDocumentTypeCatalog>();

    // Release: el checklist también lee los parámetros documentales de la compañía (aquí, ninguno).
    private static IChecklistCompanyParamsProvider CompanyParamsVacios()
    {
        var p = Substitute.For<IChecklistCompanyParamsProvider>();
        p.GetForTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Flit.Tramites.Domain.Tramites.ValueObjects.CompanyDocumentParam>>([]));
        return p;
    }
    private readonly Guid _id = Guid.NewGuid();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly List<ProcedureInstanceEvent> _events = [];

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public List<string> Deleted { get; } = [];

        public async Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_{originalFilename}";
            Saved.Add(path);
            return new StoredFile(path, "deadbeef", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Deleted.Add(storagePath);
        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    public AdminCargarDocumentoBug14604Tests()
    {
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => _events.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        // El catálogo marca como generado por el sistema el mandato condicional que el motor del checklist agrega.
        _catalog.ListSystemGeneratedCodesAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mandato", "fur" });
        _catalog.GetRuleAsync(ExternalAttachmentRules.LiquidacionImpuesto, Arg.Any<CancellationToken>())
            .Returns(new DocumentTypeRule(ExternalAttachmentRules.LiquidacionImpuesto, ["application/pdf"], 20L * 1024 * 1024));
        Matriz(("soat", "SOAT"), ("rtm", "Revisión técnico-mecánica"), ("otro", "Otros documentos"));
    }

    private void Matriz(params (string Codigo, string Nombre)[] docs) =>
        _matrix.GetForAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(docs.Select((d, i) => new ResolvedChecklistDoc(d.Codigo, d.Nombre, false, (short)i)).ToList());

    private ProcedureInstance Instance(string status, params ProcedureInstanceAttachment[] adjuntos)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = _id,
            TenantId = _tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ChecklistEstado = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var a in adjuntos)
            instance.Attachments.Add(a);
        _repo.GetByIdWithAttachmentsAsync(_id, _tenant, _ct).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(_id, _tenant, _ct).Returns(instance);
        return instance;
    }

    private ProcedureInstanceAttachment Adjunto(string tipo, string source = "user", string? provider = null, bool historico = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProcedureInstanceId = _id,
            Tipo = tipo,
            Filename = $"{tipo}_previo.pdf",
            Provider = provider,
            Source = source,
            IsHistorico = historico,
            StoragePath = $"path/{tipo}_{Guid.NewGuid():N}.pdf",
            UploadedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

    private AdminCargarDocumentoHandler Handler() =>
        new(_repo, _storage, new AdminDocumentoTiposHandler(new GetChecklistHandler(_repo, CompanyParamsVacios(), _matrix, _catalog)), new AttachmentValidator(_catalog));

    private static AdminCargarDocumentoInput Input(
        string tipo = "soat", string? observacion = "El usuario aportó el SOAT correcto", string mime = "application/pdf") =>
        new(tipo, observacion, "soat_nuevo.pdf", mime, 1024, new MemoryStream(Encoding.UTF8.GetBytes("pdf")));

    // ── Cualquier estado ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Rechazado)]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task CargaEnCualquierEstado_RegistraElAdjuntoDelUsuario_YElEventoPropio(string estado)
    {
        var instance = Instance(estado);

        var (result, error) = await Handler().HandleAsync(_id, _tenant, Input(), _userId, _ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Tipo.Should().Be("soat");
        result.PreviousAttachmentIds.Should().BeEmpty();
        instance.Attachments.Should().ContainSingle(a =>
            a.Id == result.AttachmentId && a.Source == "user" && a.UploadedBy == _userId && !a.IsHistorico);
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceAttachment>(a => a.Id == result.AttachmentId));
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _events.Should().ContainSingle(e => e.Tipo == "documento_cargado_admin" && e.CreatedBy == _userId);
    }

    [Fact]
    public async Task EnAprobado_ElGateDelGestorSigueCerrado()
    {
        AttachmentRules.AllowsUploadInState(TramiteEstado.Aprobado, "soat").Should().BeFalse(
            "la carga administrativa NO relaja el gate del gestor");
        await Task.CompletedTask;
    }

    // ── Observación obligatoria ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task ObservacionVacia_Rechaza_SinGuardarNada(string? observacion)
    {
        Instance(TramiteEstado.Aprobado);

        var (result, error) = await Handler().HandleAsync(_id, _tenant, Input(observacion: observacion), _userId, _ct);

        result.Should().BeNull();
        error.Should().Be("observacion_requerida");
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ObservacionMuyLarga_Rechaza()
    {
        Instance(TramiteEstado.Aprobado);

        var (_, error) = await Handler().HandleAsync(
            _id, _tenant, Input(observacion: new string('x', AdminCargarDocumentoHandler.MaxObservacion + 1)), _userId, _ct);

        error.Should().Be("observacion_demasiado_larga");
    }

    // ── Solo tipos habilitados para el tipo de trámite ──────────────────────────────────────────

    [Fact]
    public async Task TipoNoHabilitado_EnLaMatriz_Rechaza422_SinGuardarNada()
    {
        Instance(TramiteEstado.Aprobado);

        var (result, error) = await Handler().HandleAsync(_id, _tenant, Input(tipo: "factura"), _userId, _ct);

        result.Should().BeNull();
        error.Should().Be("tipo_no_habilitado");
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tipos_SonLosDeLaMatrizSinLosGeneradosPorElSistema_ConNombreLegible()
    {
        Instance(TramiteEstado.Aprobado);
        _matrix.GetForAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ResolvedChecklistDoc>
            {
                new("soat", "SOAT", true, 0),
                new("fur", "FUR", true, 1, EsGeneradoSistema: true),
                new("rtm", "Revisión técnico-mecánica", false, 2),
            });

        var (tipos, error) = await new AdminDocumentoTiposHandler(new GetChecklistHandler(_repo, CompanyParamsVacios(), _matrix, _catalog))
            .HandleAsync(_id, _tenant, _ct);

        error.Should().BeNull();
        tipos.Should().BeEquivalentTo([new AdminDocumentoTipoDto("soat", "SOAT"), new AdminDocumentoTipoDto("rtm", "Revisión técnico-mecánica")]);
    }

    [Fact]
    public async Task Tipos_TramiteInexistente_NotFound()
    {
        var (tipos, error) = await new AdminDocumentoTiposHandler(new GetChecklistHandler(_repo, CompanyParamsVacios(), _matrix, _catalog))
            .HandleAsync(_id, _tenant, _ct);

        tipos.Should().BeNull();
        error.Should().Be("not_found");
    }

    // ── El anterior queda histórico, no se borra ────────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task ElVigenteAnterior_QuedaHistorico_NoSeBorraNiSeQuitaDeLaInstancia(string estado)
    {
        var anterior = Adjunto("soat");
        var instance = Instance(estado, anterior);

        var (result, error) = await Handler().HandleAsync(_id, _tenant, Input(), _userId, _ct);

        error.Should().BeNull();
        result!.PreviousAttachmentIds.Should().ContainSingle().Which.Should().Be(anterior.Id);
        anterior.IsHistorico.Should().BeTrue();
        instance.Attachments.Should().Contain(anterior, "sigue visible y descargable");
        _repo.DidNotReceive().RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>());
        _storage.Deleted.Should().BeEmpty("el binario anterior se conserva");
        instance.Attachments.Count(a => a.Tipo == "soat" && !a.IsHistorico).Should().Be(1);
    }

    [Fact]
    public async Task TipoMultiple_Otro_AnadeSinRetirarNada()
    {
        var existente = Adjunto("otro");
        var instance = Instance(TramiteEstado.Aprobado, existente);

        var (result, error) = await Handler().HandleAsync(_id, _tenant, Input(tipo: "otro"), _userId, _ct);

        error.Should().BeNull();
        result!.PreviousAttachmentIds.Should().BeEmpty();
        existente.IsHistorico.Should().BeFalse();
        instance.Attachments.Count(a => a.Tipo == "otro" && !a.IsHistorico).Should().Be(2);
    }

    [Fact]
    public async Task LoGeneradoPorElSistema_NoSeMarcaHistorico()
    {
        var delSistema = Adjunto("soat", source: "system");
        Instance(TramiteEstado.Aprobado, delSistema);

        var (result, _) = await Handler().HandleAsync(_id, _tenant, Input(), _userId, _ct);

        result!.PreviousAttachmentIds.Should().BeEmpty();
        delSistema.IsHistorico.Should().BeFalse();
    }

    [Fact]
    public async Task UnHistoricoPrevio_NoSeVuelveAMarcar_NiApareceComoAnterior()
    {
        var viejo = Adjunto("soat", historico: true);
        var vigente = Adjunto("soat");
        Instance(TramiteEstado.Aprobado, viejo, vigente);

        var (result, _) = await Handler().HandleAsync(_id, _tenant, Input(), _userId, _ct);

        result!.PreviousAttachmentIds.Should().ContainSingle().Which.Should().Be(vigente.Id);
    }

    // ── Bitácora ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Evento_LlevaTipoFilenameIdsEstadoYObservacionRecortada()
    {
        var anterior = Adjunto("soat");
        Instance(TramiteEstado.Anulado, anterior);

        var (result, _) = await Handler().HandleAsync(
            _id, _tenant, Input(observacion: "   Se cargó el SOAT vigente   "), _userId, _ct);

        var evt = _events.Should().ContainSingle().Subject;
        evt.Tipo.Should().Be("documento_cargado_admin");
        evt.TenantId.Should().Be(_tenant);
        evt.ProcedureInstanceId.Should().Be(_id);
        evt.CreatedBy.Should().Be(_userId);

        using var doc = JsonDocument.Parse(evt.Payload);
        var root = doc.RootElement;
        root.GetProperty("tipo").GetString().Should().Be("soat");
        root.GetProperty("filename").GetString().Should().Be("soat_nuevo.pdf");
        root.GetProperty("attachmentId").GetGuid().Should().Be(result!.AttachmentId);
        root.GetProperty("previousAttachmentIds").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(anterior.Id);
        root.GetProperty("previousFilenames").EnumerateArray().Select(e => e.GetString()).Should().Equal("soat_previo.pdf");
        root.GetProperty("estado").GetString().Should().Be(TramiteEstado.Anulado);
        root.GetProperty("observacion").GetString().Should().Be("Se cargó el SOAT vigente");
    }

    [Fact]
    public async Task Evento_EnRevocado_NombraElArchivoQueEstabaAunqueYaFueraHistorico()
    {
        // La revocatoria deja todo histórico: el documento que se veía en la casilla es el histórico de ese tipo.
        Instance(TramiteEstado.Revocado, Adjunto("soat", historico: true), Adjunto("fur", source: "system", historico: true));

        await Handler().HandleAsync(_id, _tenant, Input(), _userId, _ct);

        using var doc = JsonDocument.Parse(_events.Should().ContainSingle().Subject.Payload);
        doc.RootElement.GetProperty("previousAttachmentIds").EnumerateArray().Should().BeEmpty();
        doc.RootElement.GetProperty("previousFilenames").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("soat_previo.pdf");
    }

    // ── Validaciones heredadas ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BloqueadoPorFlito_Responde409_SinTocarNada()
    {
        var deFlito = Adjunto(ExternalAttachmentRules.LiquidacionImpuesto, provider: ExternalAttachmentRules.Provider);
        Instance(TramiteEstado.Aprobado, deFlito);
        Matriz((ExternalAttachmentRules.LiquidacionImpuesto, "Liquidación del impuesto"));

        var (result, error) = await Handler().HandleAsync(
            _id, _tenant, Input(tipo: ExternalAttachmentRules.LiquidacionImpuesto), _userId, _ct);

        result.Should().BeNull();
        error.Should().Be(ExternalAttachmentRules.BlockedCode);
        deFlito.IsHistorico.Should().BeFalse();
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task MimeInvalido_Rechaza()
    {
        Instance(TramiteEstado.Aprobado);

        var (_, error) = await Handler().HandleAsync(_id, _tenant, Input(mime: "application/zip"), _userId, _ct);

        error.Should().Be("invalid_mime");
    }

    [Fact]
    public async Task TramiteInexistente_NotFound()
    {
        var (_, error) = await Handler().HandleAsync(_id, _tenant, Input(), _userId, _ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task SinArchivo_MissingFile()
    {
        Instance(TramiteEstado.Aprobado);
        var input = new AdminCargarDocumentoInput("soat", "obs", "x.pdf", "application/pdf", 0, new MemoryStream());

        var (_, error) = await Handler().HandleAsync(_id, _tenant, input, _userId, _ct);

        error.Should().Be("missing_file");
    }

    // ── DTO y consumidores ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Listado_DevuelveLosHistoricosMarcados()
    {
        var historico = Adjunto("soat", historico: true);
        var vigente = Adjunto("soat");
        Instance(TramiteEstado.Aprobado, historico, vigente);

        var (response, _) = await new ListAttachmentsHandler(_repo).HandleAsync(_id, _tenant, _ct);

        response!.Attachments.Should().HaveCount(2);
        response.Attachments.Single(a => a.Id == historico.Id).IsHistorico.Should().BeTrue();
        response.Attachments.Single(a => a.Id == vigente.Id).IsHistorico.Should().BeFalse();
    }

    [Fact]
    public void Efectivos_IgnoraLaVersionAnterior_PeroConservaLosHistoricosSinReemplazo()
    {
        var anterior = Adjunto("soat", historico: true);
        var vigente = Adjunto("soat");
        var revocado = Adjunto("fur", historico: true); // revocatoria: histórico sin reemplazo
        var instance = Instance(TramiteEstado.Revocado, anterior, vigente, revocado);

        instance.AdjuntosEfectivos().Should().BeEquivalentTo([vigente, revocado]);
    }

    [Fact]
    public void FurVigenciaExpediente_NoToma_ElFurHistorico()
    {
        var marca = DateTimeOffset.UtcNow.AddHours(-1);
        var furVigente = Adjunto("fur", source: "system");
        furVigente.UploadedAt = marca.AddMinutes(10); // posterior a la marca: al día
        var furHistorico = Adjunto("fur", source: "system", historico: true);
        furHistorico.UploadedAt = marca.AddMinutes(-30); // desactualizado: si se tomara, el FUR saldría vencido
        var instance = Instance(TramiteEstado.Borrador, furVigente, furHistorico);
        instance.ExpedienteActualizadoEn = marca;

        FurVigenciaExpediente.FurDesactualizado(instance).Should().BeFalse();
    }

    [Fact]
    public async Task SubidaDelGestor_NoBorraLaVersionAnteriorHistorica()
    {
        var historico = Adjunto("soat", historico: true);
        var instance = Instance(TramiteEstado.Borrador, historico);
        var upload = new UploadAttachmentHandler(_repo, _storage);

        var (dto, error) = await upload.HandleAsync(
            _id, _tenant, new UploadAttachmentInput("soat", "n.pdf", "application/pdf", 10, new MemoryStream([1, 2, 3])), _userId, _ct);

        error.Should().BeNull();
        dto!.IsHistorico.Should().BeFalse();
        instance.Attachments.Should().Contain(historico);
        _repo.DidNotReceive().RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>());
        _storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Tracking_ProyectaElEventoConCamposAplanados()
    {
        var evt = new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant,
            ProcedureInstanceId = _id,
            Tipo = "documento_cargado_admin",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = _userId,
            Payload = """{"tipo":"soat","filename":"soat.pdf","attachmentId":"00000000-0000-0000-0000-000000000001","previousAttachmentIds":[],"estado":"aprobado","observacion":"Corrección"}""",
        };

        var dtos = await GetProcedureInstanceHandler.BuildEventsAsync(_repo, [evt], _ct);

        var dto = dtos.Should().ContainSingle().Subject;
        dto.Tipo.Should().Be("documento_cargado_admin");
        dto.DocumentoTipo.Should().Be("soat");
        dto.DocumentoFilename.Should().Be("soat.pdf");
        dto.EstadoAlMomento.Should().Be("aprobado");
        dto.Observacion.Should().Be("Corrección");
    }
}

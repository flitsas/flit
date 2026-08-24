using System.Text;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class PortalHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeStorage _storage = new();
    private readonly GetPortalByTokenHandler _view;
    private readonly AceptarConsentimientoHandler _consent;
    private readonly SubirDocumentoPortalHandler _upload;
    private readonly FinalizarPortalHandler _finalizar;
    private readonly SimularFirmaPortalHandler _firmaPortal;
    private readonly GetFirmaUrlPortalHandler _firmaUrl;

    public PortalHandlerTests()
    {
        _view = new GetPortalByTokenHandler(_repo);
        _consent = new AceptarConsentimientoHandler(_repo);
        _upload = new SubirDocumentoPortalHandler(_repo, _storage);
        _finalizar = new FinalizarPortalHandler(_repo);
        _firmaPortal = new SimularFirmaPortalHandler(_repo, new SimularFirmaHandler(_repo));
        _firmaUrl = new GetFirmaUrlPortalHandler(_repo);
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}";
            Saved.Add(path);
            return new StoredFile(path, "deadbeef", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) { }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private static ProcedureInstance Instance(string tipologia = "traspaso_standard") =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For(tipologia ?? (tipologia == "traspaso_standard" ? "traspaso" : "matricula_inicial")),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000042",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = tipologia == "traspaso_standard" ? "traspaso" : "matricula_inicial",
            TipologiaCodigo = tipologia,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Crea un participante activo (consentido opcional) y lo cablea al lookup por hash.</summary>
    private (ProcedureInstanceParticipant p, string token) Seed(
        string rol = "comprador",
        bool consent = false,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? completedAt = null,
        ProcedureInstance? instance = null)
    {
        var inst = instance ?? Instance();
        var token = $"raw-{Guid.NewGuid():N}";
        var p = new ProcedureInstanceParticipant
        {
            Id = Guid.NewGuid(),
            TenantId = inst.TenantId,
            ProcedureInstanceId = inst.Id,
            Rol = rol,
            Nombre = "Juan",
            Email = "j@x.com",
            TokenHash = PortalToken.Hash(token),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
            CompletedAt = completedAt,
            Consent1581At = consent ? DateTimeOffset.UtcNow : null,
            ConsentVersion = consent ? ParticipantRules.ConsentVersion : null,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcedureInstance = inst,
        };
        _repo.GetParticipantByTokenHashAsync(PortalToken.Hash(token), Arg.Any<CancellationToken>()).Returns(p);
        return (p, token);
    }

    private static MemoryStream Doc(string content = "pdf") => new(Encoding.UTF8.GetBytes(content));

    // ── Vista del portal ───────────────────────────────────────────────────────

    [Fact]
    public async Task View_Active_AggregatesPendingSteps()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, token) = Seed(rol: "comprador");

        var (result, error) = await _view.HandleAsync(token, ct);

        error.Should().BeNull();
        result!.Rol.Should().Be("comprador");
        result.ConsentDado.Should().BeFalse();
        result.ConsentVersion.Should().Be(ParticipantRules.ConsentVersion);
        result.ConsentText.Should().Contain("Ley 1581");
        result.Instancia.Referencia.Should().Be("TRM-2026-000042");
        result.PasosPendientes.Documentos.Should().NotBeEmpty();
        // Traspaso + comprador: la firma aplica.
        result.PasosPendientes.Firma.Aplica.Should().BeTrue();
        // Sin biométrica creada aún → pendiente (admin debe crearla).
        result.PasosPendientes.Biometrica.Existe.Should().BeFalse();
        result.PasosPendientes.Biometrica.Pendiente.Should().BeTrue();
    }

    [Fact]
    public async Task View_UploadedDoc_MarksSubido()
    {
        var ct = TestContext.Current.CancellationToken;
        var inst = Instance();
        inst.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = inst.TenantId,
            ProcedureInstanceId = inst.Id,
            Tipo = "soat",
            Filename = "s.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 1,
            Sha256 = "x",
            StoragePath = "p",
            Source = "portal",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        var (_, token) = Seed(instance: inst);

        var (result, _) = await _view.HandleAsync(token, ct);

        result!.PasosPendientes.Documentos.Should().Contain(d => d.Tipo == "soat" && d.Subido);
    }

    [Fact]
    public async Task View_TokenNotFound_DoesNotLeak()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetParticipantByTokenHashAsync(Arg.Any<string>(), ct).Returns((ProcedureInstanceParticipant?)null);

        var (result, error) = await _view.HandleAsync("whatever", ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task View_Expired_ReturnsNotFoundGenerically()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, token) = Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));

        var (result, error) = await _view.HandleAsync(token, ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task View_Completed_ReturnsNotFound_TokenRevoked()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, token) = Seed(completedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var (_, error) = await _view.HandleAsync(token, ct);

        error.Should().Be("not_found");
    }

    // ── Consentimiento ───────────────────────────────────────────────────────

    [Fact]
    public async Task Consent_PersistsVersionIpAndTruncatedUa()
    {
        var ct = TestContext.Current.CancellationToken;
        var (p, token) = Seed();
        var longUa = new string('U', 300);

        var (result, error) = await _consent.HandleAsync(token, new AceptarConsentimientoInput("1.2.3.4", longUa), ct);

        error.Should().BeNull();
        result!.ConsentVersion.Should().Be(ParticipantRules.ConsentVersion);
        p.Consent1581At.Should().NotBeNull();
        p.ConsentVersion.Should().Be(ParticipantRules.ConsentVersion);
        p.ConsentIp.Should().Be("1.2.3.4");
        p.ConsentUserAgent!.Length.Should().Be(ParticipantRules.UserAgentMaxLength);
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "consentimiento_1581"), ct);
    }

    [Fact]
    public async Task Consent_TokenNotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetParticipantByTokenHashAsync(Arg.Any<string>(), ct).Returns((ProcedureInstanceParticipant?)null);

        var (_, error) = await _consent.HandleAsync("x", new AceptarConsentimientoInput(null, null), ct);

        error.Should().Be("not_found");
    }

    // ── Subir documento (gate de consentimiento) ───────────────────────────────

    [Fact]
    public async Task Upload_WithoutConsent_Rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, token) = Seed(consent: false);

        var (result, error) = await _upload.HandleAsync(
            token, new SubirDocumentoPortalInput("soat", "s.pdf", "application/pdf", 4, Doc()), ct);

        error.Should().Be("consent_requerido");
        result.Should().BeNull();
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_WithConsent_StoresAttachment()
    {
        var ct = TestContext.Current.CancellationToken;
        var inst = Instance();
        var (_, token) = Seed(consent: true, instance: inst);

        var (result, error) = await _upload.HandleAsync(
            token, new SubirDocumentoPortalInput("soat", "s.pdf", "application/pdf", 4, Doc()), ct);

        error.Should().BeNull();
        result!.Tipo.Should().Be("soat");
        result.Source.Should().Be("portal");
        inst.Attachments.Should().ContainSingle();
        _storage.Saved.Should().HaveCount(1);
        _repo.Received(1).Add(Arg.Any<ProcedureInstanceAttachment>());
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "documento_subido"), ct);
    }

    [Fact]
    public async Task Upload_InvalidTipo_Rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, token) = Seed(consent: true);

        var (_, error) = await _upload.HandleAsync(
            token, new SubirDocumentoPortalInput("nope", "s.pdf", "application/pdf", 4, Doc()), ct);

        error.Should().Be("invalid_tipo");
    }

    // ── Finalizar (uso único) ──────────────────────────────────────────────────

    [Fact]
    public async Task Finalizar_RevokesToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var (p, token) = Seed();

        var (result, error) = await _finalizar.HandleAsync(token, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        p.CompletedAt.Should().NotBeNull();
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "portal_finalizado"), ct);
    }

    [Fact]
    public async Task Finalizar_ThenView_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var (p, token) = Seed();

        await _finalizar.HandleAsync(token, ct);
        // El estado del participante quedó completado → un acceso posterior con el mismo token falla.
        p.CompletedAt.Should().NotBeNull();
        var (_, error) = await _view.HandleAsync(token, ct);

        error.Should().Be("not_found");
    }

    // ── Firma passthrough (reusa SimularFirmaHandler) ──────────────────────────

    [Fact]
    public async Task FirmaPortal_CompletesViaSimularFirmaHandler()
    {
        var ct = TestContext.Current.CancellationToken;
        var inst = Instance();
        var sig = new ProcedureInstanceSignature
        {
            Id = Guid.NewGuid(),
            TenantId = inst.TenantId,
            ProcedureInstanceId = inst.Id,
            Parte = "comprador",
            DocTipo = "compraventa",
            Proveedor = "mock",
            Estado = SignatureEstados.Enviada,
            EnvelopeId = "env-1",
            SolicitadoAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        inst.Signatures.Add(sig);
        var (_, token) = Seed(rol: "comprador", instance: inst);
        // SimularFirmaHandler interno re-consulta por GetByIdWithSignaturesAsync.
        _repo.GetByIdWithSignaturesAsync(inst.Id, inst.TenantId, Arg.Any<CancellationToken>()).Returns(inst);

        var (result, error) = await _firmaPortal.HandleAsync(token, ct);

        error.Should().BeNull();
        result!.Estado.Should().Be(SignatureEstados.Firmada);
        sig.Estado.Should().Be(SignatureEstados.Firmada);
        sig.FirmadoAt.Should().NotBeNull();
    }

    [Fact]
    public async Task FirmaPortal_NoSignatureRequested_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var inst = Instance();
        var (_, token) = Seed(rol: "comprador", instance: inst);

        var (_, error) = await _firmaPortal.HandleAsync(token, ct);

        error.Should().Be("firma_no_solicitada");
    }

    [Fact]
    public async Task FirmaUrl_NotApplicableForMandatario()
    {
        var ct = TestContext.Current.CancellationToken;
        var inst = Instance();
        var (_, token) = Seed(rol: "mandatario", instance: inst);

        var (result, error) = await _firmaUrl.HandleAsync(token, ct);

        error.Should().BeNull();
        result!.Aplica.Should().BeFalse();
    }
}

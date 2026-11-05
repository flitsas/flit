using System.Text;
using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #14302 (Feature #14300, Épica #14271) — la carta de identidad de una validación MANUAL entra al expediente (mismo tipo y
/// posición que el certificado de Kyverum), al sello del FUR y a la descarga. Kyverum no cambia.
/// </summary>
public sealed class CartaIdentidadExpedienteTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly BlobStorage _storage = new();
    private readonly FakeCertClient _cert = new();
    private readonly FakeLetters _letters;
    private readonly CapturingFur _fur = new();
    private readonly Guid _tenant = Guid.NewGuid();

    public CartaIdentidadExpedienteTests()
    {
        _letters = new FakeLetters(_repo, _storage);
        _repo.GetBiometricByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => _letters.Find(ci.Arg<Guid>()));
    }

    // ── Fakes ──────────────────────────────────────────────────────────────────────────────────────

    private sealed class BlobStorage : IAttachmentStorage
    {
        public Dictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, byte[]> SavedByTipo { get; } = new(StringComparer.Ordinal);

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            SavedByTipo[tipo] = ms.ToArray();
            return new StoredFile($"{procedureInstanceId:D}/{tipo}", $"sha-{tipo}", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) { }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Blobs.TryGetValue(storagePath, out var b) ? new MemoryStream(b) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private sealed class FakeCertClient : IKyverumCertificateClient
    {
        public List<string> RequestedIds { get; } = [];

        public Task<KyverumCertificate?> DownloadCertificateAsync(string verificationId, CancellationToken ct = default)
        {
            RequestedIds.Add(verificationId);
            return Task.FromResult<KyverumCertificate?>(
                new KyverumCertificate(Encoding.UTF8.GetBytes($"KYV:{verificationId}"), "application/pdf", $"c_{verificationId}.pdf"));
        }
    }

    /// <summary>Genera la carta como el servicio real: registra serie/hash/ruta en la validación y deja el PDF en el storage.</summary>
    private sealed class FakeLetters(IProcedureInstanceRepository repo, BlobStorage storage) : IIdentityLetterService
    {
        private readonly Dictionary<Guid, ProcedureInstanceBiometricValidation> _byId = [];
        public int Calls { get; private set; }
        public bool Fail { get; set; }

        public void Track(ProcedureInstanceBiometricValidation v) => _byId[v.Id] = v;
        public ProcedureInstanceBiometricValidation? Find(Guid id) => _byId.GetValueOrDefault(id);

        public Task<IdentityLetterOutcome> EnsureForValidationAsync(Guid validationId, CancellationToken ct = default)
        {
            Calls++;
            if (Fail)
                throw new IdentityLetterGenerationException("storage caído");
            var v = _byId[validationId];
            if (!v.CorrespondeCartaIdentidad)
                return Task.FromResult(IdentityLetterOutcome.Skipped);
            if (v.TieneCartaIdentidad)
                return Task.FromResult(IdentityLetterOutcome.AlreadyPresent);
            Seed(v, $"CARTA-GENERADA:{v.DocumentNumber}");
            return Task.FromResult(IdentityLetterOutcome.Generated);
        }

        public void Seed(ProcedureInstanceBiometricValidation v, string pdf)
        {
            var path = $"cartas/{v.Id:N}.pdf";
            storage.Blobs[path] = Encoding.UTF8.GetBytes(pdf);
            v.RegistrarCartaIdentidad($"FL-{v.DocumentNumber.PadRight(8, '0')[..8]}-ABCDEF", new string('a', 64), new string('b', 64), path,
                DateTimeOffset.UtcNow);
            _ = repo;
        }
    }

    private sealed class CapturingFur : IFurDocumentGenerator
    {
        public FurDocumentData? Captured { get; private set; }

        public GeneratedDocument GenerateFur(FurDocumentData data)
        {
            Captured = data;
            return new GeneratedDocument("fur", "fur.pdf", "application/pdf", [1, 2, 3]);
        }

        public GeneratedDocument GenerateFurFillAll(FurTemplateFormat format = FurTemplateFormat.Automotor) =>
            new("fur", "fur_FILLALL.pdf", "application/pdf", [1, 2, 3]);

        public GeneratedDocument GenerateCompraventa(FurDocumentData data) =>
            new("compraventa", "cv.pdf", "application/pdf", [1, 2, 3]);
    }

    // ── Fixtures ───────────────────────────────────────────────────────────────────────────────────

    private GenerarFurHandler Handler(bool conLetters = true) =>
        new(_repo, _fur, _cert, Substitute.For<IRuesCertificateGenerator>(), Substitute.For<IRnmcCertificateGenerator>(),
            Substitute.For<IProcedureInstancePrendaRepository>(), _storage, NullLogger<GenerarFurHandler>.Instance,
            identityLetterService: conLetters ? _letters : null);

    private ProcedureInstance Traspaso()
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard),
            Id = Guid.NewGuid(),
            TenantId = _tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant,
            ProcedureInstanceId = instance.Id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });
        _repo.GetByIdWithFurGraphAsync(instance.Id, _tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private ProcedureInstanceActor Actor(ProcedureInstance i, string rol, string doc, int ordinal = 1) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant,
            ProcedureInstanceId = i.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = rol,
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = $"{rol} {doc}",
            Ordinal = ordinal,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private ProcedureInstanceBiometricValidation Bio(
        string? rol, string doc, bool manual, string? carta = null, string? kyverumId = null, Guid? instanceId = null)
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant,
            ProcedureInstanceId = instanceId,
            PartyRole = rol,
            Status = BiometricEstados.Aprobado,
            Provider = manual ? BiometricProviders.Manual : BiometricProviders.Kyverum,
            ApprovalOrigin = manual ? BiometricApprovalOrigins.Manual : BiometricApprovalOrigins.Automatica,
            KyverumVerificationId = manual ? null : kyverumId ?? $"kyv-{doc}",
            Name = "X",
            DocumentType = "CC",
            DocumentNumber = doc,
            Email = "x@y.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
            ValidatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ValidUntil = DateTimeOffset.UtcNow.AddDays(29),
        };
        _letters.Track(v);
        if (carta is not null)
            _letters.Seed(v, carta);
        return v;
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    // ── AC1: la carta entra al expediente con el tipo y la posición del certificado ────────────────

    [Fact]
    public async Task Ac1_ManualConCarta_AdjuntaLaCartaConElTipoDelCertificadoYSinTocarKyverum()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Traspaso();
        i.Actors.Add(Actor(i, "comprador", "111"));
        i.Actors.Add(Actor(i, "vendedor", "333"));
        i.BiometricValidations.Add(Bio("comprador", "111", manual: true, carta: "CARTA-COMPRADOR"));
        i.BiometricValidations.Add(Bio("vendedor", "333", manual: true, carta: "CARTA-VENDEDOR"));

        var (result, error) = await Handler().HandleAsync(i.Id, _tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(
            ["fur", "certificado_identidad", "certificado_identidad_vendedor", "compraventa"]);
        Text(_storage.SavedByTipo["certificado_identidad"]).Should().Be("CARTA-COMPRADOR");
        Text(_storage.SavedByTipo["certificado_identidad_vendedor"]).Should().Be("CARTA-VENDEDOR");
        _cert.RequestedIds.Should().BeEmpty("la carta de FLIT no consulta a Kyverum");
        _letters.Calls.Should().Be(0, "la carta ya existía: no se genera de nuevo");
    }

    [Fact]
    public void Ac1_LaCartaOcupaLaMismaPosicionYEtiquetaQueElCertificado()
    {
        // La carta reutiliza el tipo del certificado: el consolidado la ordena y rotula con las mismas reglas.
        IdentityCertificateAttachmentTipo.For("comprador", 2).Should().Be("certificado_identidad_2");
        DocumentLabels.Display("certificado_identidad").Should().Be("Certificado de validación de identidad");
        DocumentLabels.Display("certificado_identidad_vendedor").Should().Be("Certificado de identidad (vendedor)");
    }

    // ── AC1.1: copropietarios — cada actor, SU carta ─────────────────────────────────────────────────

    [Fact]
    public async Task Ac1_1_Copropietarios_CadaActorTieneSuCartaYNoSeCruzan()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Traspaso();
        i.Actors.Add(Actor(i, "comprador", "111", ordinal: 1));
        i.Actors.Add(Actor(i, "comprador", "222", ordinal: 2));
        i.Actors.Add(Actor(i, "vendedor", "333"));
        // Orden de la lista a propósito invertido: la asociación es por documento, no por posición.
        i.BiometricValidations.Add(Bio("comprador", "222", manual: true, carta: "CARTA-222"));
        i.BiometricValidations.Add(Bio("comprador", "111", manual: true, carta: "CARTA-111"));
        i.BiometricValidations.Add(Bio("vendedor", "333", manual: true, carta: "CARTA-333"));

        var (result, error) = await Handler().HandleAsync(i.Id, _tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(
            ["fur", "certificado_identidad", "certificado_identidad_2", "certificado_identidad_vendedor", "compraventa"]);
        Text(_storage.SavedByTipo["certificado_identidad"]).Should().Be("CARTA-111");
        Text(_storage.SavedByTipo["certificado_identidad_2"]).Should().Be("CARTA-222");
        Text(_storage.SavedByTipo["certificado_identidad_vendedor"]).Should().Be("CARTA-333");
    }

    [Fact]
    public async Task Ac1_1_ActorSinSuPropiaValidacion_NoRecibeLaCartaDeOtro_FailClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Traspaso();
        i.Actors.Add(Actor(i, "comprador", "111", ordinal: 1));
        i.Actors.Add(Actor(i, "comprador", "222", ordinal: 2)); // sin validación manual propia
        i.Actors.Add(Actor(i, "vendedor", "333"));
        i.BiometricValidations.Add(Bio("comprador", "111", manual: true, carta: "CARTA-111"));
        i.BiometricValidations.Add(Bio("vendedor", "333", manual: true, carta: "CARTA-333"));

        var (result, error) = await Handler().HandleAsync(i.Id, _tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_identidad_2");
        // La carta del 111 jamás aparece bajo otro tipo (el gate de identidad puede incluso no emitir ninguna: lo que importa es no cruzar).
        _storage.SavedByTipo.Where(kv => Text(kv.Value) == "CARTA-111").Select(kv => kv.Key)
            .Should().NotContain(k => k != "certificado_identidad");
    }

    // ── AC1.2: persona jurídica — la carta es la del representante legal ────────────────────────────

    [Fact]
    public async Task Ac1_2_PersonaJuridica_LaCartaEsLaDelRepresentanteLegalYSeAsociaAEseActor()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Traspaso();
        var empresa = Actor(i, "comprador", "900123456");
        empresa.DocumentType = "NIT";
        empresa.PersonType = "juridical";
        empresa.Metadata = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["representanteLegal"] = new Dictionary<string, object?>
            {
                ["tipoDocumento"] = "CC",
                ["numeroDocumento"] = "52082029",
                ["nombreCompleto"] = "REPRESENTANTE DEMO",
            },
        });
        i.Actors.Add(empresa);
        i.Actors.Add(Actor(i, "vendedor", "333"));
        i.BiometricValidations.Add(Bio("comprador", "52082029", manual: true, carta: "CARTA-RL"));
        i.BiometricValidations.Add(Bio("vendedor", "333", manual: true, carta: "CARTA-333"));

        var (result, error) = await Handler().HandleAsync(i.Id, _tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().Contain("certificado_identidad");
        Text(_storage.SavedByTipo["certificado_identidad"]).Should().Be("CARTA-RL");
    }

    // ── AC1.3: proveedores distintos — cada actor conserva su documento ────────────────────────────

    [Fact]
    public async Task Ac1_3_CompradorKyverumYVendedorManual_CadaUnoConservaSuDocumento()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Traspaso();
        i.Actors.Add(Actor(i, "comprador", "111"));
        i.Actors.Add(Actor(i, "vendedor", "333"));
        i.BiometricValidations.Add(Bio("comprador", "111", manual: false, kyverumId: "kyv-comprador"));
        i.BiometricValidations.Add(Bio("vendedor", "333", manual: true, carta: "CARTA-VENDEDOR"));

        var (result, error) = await Handler().HandleAsync(i.Id, _tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(
            ["fur", "certificado_identidad", "certificado_identidad_vendedor", "compraventa"]);
        Text(_storage.SavedByTipo["certificado_identidad"]).Should().Be("KYV:kyv-comprador");
        Text(_storage.SavedByTipo["certificado_identidad_vendedor"]).Should().Be("CARTA-VENDEDOR");
        _cert.RequestedIds.Should().Equal("kyv-comprador");
    }

    // ── AC2: identidad apalancada ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ac2_IdentidadManualVigenteDeOtroTramite_DaLaMismaCarta()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Traspaso();
        i.Actors.Add(Actor(i, "comprador", "111"));
        i.Actors.Add(Actor(i, "vendedor", "333"));
        // Las dos identidades viven en OTRO trámite / prevalidación (sin fila propia en este trámite).
        var deOtro = Bio("comprador", "111", manual: true, carta: "CARTA-COMPARTIDA", instanceId: Guid.NewGuid());
        var prevalidacion = Bio(null, "333", manual: true, carta: "CARTA-PREVALIDACION");
        _repo.FindVigenteApprovedByDocumentAsync(_tenant, "CC", "111", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(deOtro);
        _repo.FindVigenteApprovedByDocumentAsync(_tenant, "CC", "333", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(prevalidacion);

        var (result, error) = await Handler().HandleAsync(i.Id, _tenant, ct);

        error.Should().BeNull();
        Text(_storage.SavedByTipo["certificado_identidad"]).Should().Be("CARTA-COMPARTIDA");
        Text(_storage.SavedByTipo["certificado_identidad_vendedor"]).Should().Be("CARTA-PREVALIDACION");
        result!.Documents.Select(d => d.Tipo).Should().Contain(["certificado_identidad", "certificado_identidad_vendedor"]);
    }

    // ── Generación bajo demanda ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BajoDemanda_CartaFaltante_SeGeneraAlArmarElExpedienteYElSelloLaLleva()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Traspaso();
        i.Actors.Add(Actor(i, "comprador", "111"));
        i.Actors.Add(Actor(i, "vendedor", "333"));
        var comprador = Bio("comprador", "111", manual: true);   // la outbox se rindió: sin carta
        i.BiometricValidations.Add(comprador);
        i.BiometricValidations.Add(Bio("vendedor", "333", manual: true, carta: "CARTA-333"));

        var (result, error) = await Handler().HandleAsync(i.Id, _tenant, ct);

        error.Should().BeNull();
        _letters.Calls.Should().Be(1, "solo la del comprador faltaba (idempotente: una sola vez)");
        comprador.TieneCartaIdentidad.Should().BeTrue();
        Text(_storage.SavedByTipo["certificado_identidad"]).Should().Be("CARTA-GENERADA:111");
        result!.Documents.Select(d => d.Tipo).Should().Contain("certificado_identidad");
        // AC3 — el sello se arma DESPUÉS de garantizar la carta: lleva su serie, no «Firma no disponible».
        _fur.Captured!.SellosIdentidad!["comprador"].Should().Contain(comprador.CartaSerie!).And.NotContain("no disponible");
    }

    [Fact]
    public async Task BajoDemanda_SiNoSePuedeGenerar_ElFurSeGeneraIgualYSeOmiteLaCarta_ComoElCertificadoDeKyverum()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Traspaso();
        i.Actors.Add(Actor(i, "comprador", "111"));
        i.Actors.Add(Actor(i, "vendedor", "333"));
        i.BiometricValidations.Add(Bio("comprador", "111", manual: true));
        i.BiometricValidations.Add(Bio("vendedor", "333", manual: true, carta: "CARTA-333"));
        _letters.Fail = true;

        var (result, error) = await Handler().HandleAsync(i.Id, _tenant, ct);

        error.Should().BeNull("el fallo del certificado nunca bloquea el FUR");
        result!.Documents.Select(d => d.Tipo).Should().Contain("fur").And.NotContain("certificado_identidad");
        result.Documents.Select(d => d.Tipo).Should().Contain("certificado_identidad_vendedor");
        _fur.Captured!.SellosIdentidad!["comprador"].Should().Contain("no disponible");
    }

    [Fact]
    public async Task SinServicioDeCartas_ElFurNoCambia_YNoLanza()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Traspaso();
        i.Actors.Add(Actor(i, "comprador", "111"));
        i.Actors.Add(Actor(i, "vendedor", "333"));
        i.BiometricValidations.Add(Bio("comprador", "111", manual: true));
        i.BiometricValidations.Add(Bio("vendedor", "333", manual: true));

        var (result, error) = await Handler(conLetters: false).HandleAsync(i.Id, _tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_identidad");
    }

    // ── AC3: sello del FUR ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ac3_SelloDeIdentidadManual_MuestraSoloLaSerieDeLaCarta_SinHash()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Traspaso();
        i.Actors.Add(Actor(i, "comprador", "111"));
        i.Actors.Add(Actor(i, "vendedor", "333"));
        var comprador = Bio("comprador", "111", manual: true, carta: "CARTA-111");
        i.BiometricValidations.Add(comprador);
        i.BiometricValidations.Add(Bio("vendedor", "333", manual: false, kyverumId: "kyv-v"));

        var (_, error) = await Handler().HandleAsync(i.Id, _tenant, ct);

        error.Should().BeNull();
        var sello = _fur.Captured!.SellosIdentidad!["comprador"];
        sello.Should().Contain($"Firma {comprador.CartaSerie}\n").And.NotContain(comprador.CartaSha256!).And.NotContain("no disponible");
    }

    [Fact]
    public void Ac3_SelloKyverum_NoCambia()
    {
        var kyverum = Bio("comprador", "111", manual: false);
        kyverum.CertificateHash = "KV-ABC123";

        IdentidadSelloText.Build(kyverum).Should().Contain("Firma KV-ABC123\n");
    }

    [Fact]
    public void Ac3_ManualSinCartaTodavia_SigueDiciendoFirmaNoDisponible()
    {
        IdentidadSelloText.Build(Bio("comprador", "111", manual: true)).Should().Contain("Firma no disponible");
    }

    [Fact]
    public void Ac3_CertificateHashDeKyverumGanaSiExisten_Ambos()
    {
        var v = Bio("comprador", "111", manual: true, carta: "C");
        v.CertificateHash = "KV-XYZ";

        IdentidadSelloText.Build(v).Should().Contain("Firma KV-XYZ\n").And.NotContain("FL-");
    }

    // ── AC4: descarga ─────────────────────────────────────────────────────────────────────────────

    private DescargarCertificadoIdentidadHandler Descarga() =>
        new(_repo, _cert, signatureCapture: null, letters: _letters, storage: _storage);

    [Fact]
    public async Task Ac4_Descarga_ManualConCarta_DevuelveElPdfDeLaCarta()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var v = Bio("comprador", "111", manual: true, carta: "CARTA-111", instanceId: instanceId);

        var (result, error) = await Descarga().HandleAsync(instanceId, _tenant, v.Id, ct);

        error.Should().BeNull();
        result!.ContentType.Should().Be("application/pdf");
        result.FileName.Should().Be($"carta_identidad_{v.CartaSerie}.pdf");
        Text(result.Content).Should().Be("CARTA-111");
        _cert.RequestedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Ac4_Descarga_CartaFaltante_SeGeneraEnElMomento()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var v = Bio("comprador", "111", manual: true, instanceId: instanceId);

        var (result, error) = await Descarga().HandleAsync(instanceId, _tenant, v.Id, ct);

        error.Should().BeNull();
        _letters.Calls.Should().Be(1);
        Text(result!.Content).Should().Be("CARTA-GENERADA:111");
    }

    [Fact]
    public async Task Ac4_Descarga_SiNoSePuedeObtener_ErrorClaroParaReintentar_NoProveedorError()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var v = Bio("comprador", "111", manual: true, instanceId: instanceId);
        _letters.Fail = true;

        var (result, error) = await Descarga().HandleAsync(instanceId, _tenant, v.Id, ct);

        result.Should().BeNull();
        error.Should().Be("carta_no_disponible").And.NotBe("proveedor_error");
    }

    [Fact]
    public async Task Ac4_Descarga_ArchivoAusenteEnStorage_ErrorParaReintentar()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var v = Bio("comprador", "111", manual: true, carta: "X", instanceId: instanceId);
        _storage.Blobs.Clear();

        var (_, error) = await Descarga().HandleAsync(instanceId, _tenant, v.Id, ct);

        error.Should().Be("carta_no_disponible");
    }

    [Fact]
    public async Task Ac4_Descarga_OtroTenant_NoSeExpone()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var v = Bio("comprador", "111", manual: true, carta: "CARTA", instanceId: instanceId);

        var (result, error) = await Descarga().HandleAsync(instanceId, Guid.NewGuid(), v.Id, ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Ac4_Descarga_ValidacionDeOtroTramiteQueNoEsIdentidadEfectiva_NoSeExpone()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var v = Bio("comprador", "999", manual: true, carta: "CARTA", instanceId: Guid.NewGuid());
        _repo.GetByIdWithBiometricsAndActorsAsync(instanceId, _tenant, Arg.Any<CancellationToken>()).Returns(
            new ProcedureInstance
            {
                ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard),
                Id = instanceId,
                TenantId = _tenant,
                Actors = [Actor(new ProcedureInstance { Id = instanceId }, "comprador", "111")],
            });

        var (_, error) = await Descarga().HandleAsync(instanceId, _tenant, v.Id, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Ac4_Descarga_ManualVigenteDeOtroTramite_ConDocumentoDelActor_DevuelveLaCarta()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var v = Bio("comprador", "111", manual: true, carta: "CARTA-APALANCADA", instanceId: Guid.NewGuid());
        _repo.GetByIdWithBiometricsAndActorsAsync(instanceId, _tenant, Arg.Any<CancellationToken>()).Returns(
            new ProcedureInstance
            {
                ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard),
                Id = instanceId,
                TenantId = _tenant,
                Actors = [Actor(new ProcedureInstance { Id = instanceId }, "comprador", "111")],
            });

        var (result, error) = await Descarga().HandleAsync(instanceId, _tenant, v.Id, ct);

        error.Should().BeNull();
        Text(result!.Content).Should().Be("CARTA-APALANCADA");
    }

    [Fact]
    public async Task Ac4_Descarga_ManualRechazada_SinCertificado()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var v = Bio("comprador", "111", manual: true, instanceId: instanceId);
        v.Status = BiometricEstados.Rechazado;

        var (_, error) = await Descarga().HandleAsync(instanceId, _tenant, v.Id, ct);

        error.Should().Be("sin_certificado");
    }

    // ── Descarga SIN trámite (por id de validación) ───────────────────────────────────────────────

    [Fact]
    public async Task PorValidacion_SuperAdminSinTenant_ServeLaCartaDeCualquierCompania()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Bio(null, "111", manual: true, carta: "CARTA-PREVALIDACION");

        var (result, error) = await Descarga().HandleCartaPorValidacionAsync(v.Id, tenantId: null, ct);

        error.Should().BeNull();
        Text(result!.Content).Should().Be("CARTA-PREVALIDACION");
        result.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task PorValidacion_MismoTenant_GeneraBajoDemandaSiFalta()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Bio(null, "111", manual: true);

        var (result, error) = await Descarga().HandleCartaPorValidacionAsync(v.Id, _tenant, ct);

        error.Should().BeNull();
        _letters.Calls.Should().Be(1);
        Text(result!.Content).Should().Be("CARTA-GENERADA:111");
    }

    [Fact]
    public async Task PorValidacion_OtroTenant_Responde404SinRevelarQueExiste()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Bio(null, "111", manual: true, carta: "C");

        var (result, error) = await Descarga().HandleCartaPorValidacionAsync(v.Id, Guid.NewGuid(), ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
        _letters.Calls.Should().Be(0, "ni siquiera intenta generar la carta de otro tenant");
    }

    [Fact]
    public async Task PorValidacion_NoManualNoAprobadaOInexistente_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var kyverum = Bio(null, "1", manual: false);
        var rechazada = Bio(null, "2", manual: true);
        rechazada.Status = BiometricEstados.Rechazado;

        (await Descarga().HandleCartaPorValidacionAsync(kyverum.Id, null, ct)).Error.Should().Be("not_found");
        (await Descarga().HandleCartaPorValidacionAsync(rechazada.Id, null, ct)).Error.Should().Be("not_found");
        (await Descarga().HandleCartaPorValidacionAsync(Guid.NewGuid(), null, ct)).Error.Should().Be("not_found");
    }

    [Fact]
    public async Task PorValidacion_SiNoSePuedeObtener_CartaNoDisponible()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Bio(null, "111", manual: true);
        _letters.Fail = true;

        var (result, error) = await Descarga().HandleCartaPorValidacionAsync(v.Id, null, ct);

        result.Should().BeNull();
        error.Should().Be("carta_no_disponible");
    }

    // ── Contrato: cartaDisponible ─────────────────────────────────────────────────────────────────

    private async Task<BiometricValidationDto> Detalle(ProcedureInstanceBiometricValidation v)
    {
        _repo.ListLinkedProceduresByIdentityDocumentsAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<(string, string)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, IReadOnlyList<LinkedProcedureSummary>>());
        var (dto, error) = await new GetPrevalidacionDetailHandler(_repo)
            .HandleAsync(_tenant, v.Id, TestContext.Current.CancellationToken);
        error.Should().BeNull();
        return dto!;
    }

    [Fact]
    public async Task Contrato_CartaDisponible_SoloManualAprobadaConCarta()
    {
        (await Detalle(Bio("comprador", "1", manual: true, carta: "C"))).CartaDisponible.Should().BeTrue();
        (await Detalle(Bio("comprador", "2", manual: true))).CartaDisponible.Should().BeFalse("aún sin carta");
        (await Detalle(Bio("comprador", "3", manual: false))).CartaDisponible.Should().BeFalse("Kyverum");

        var rechazada = Bio("comprador", "4", manual: true);
        rechazada.Status = BiometricEstados.Rechazado;
        (await Detalle(rechazada)).CartaDisponible.Should().BeFalse();
    }
}

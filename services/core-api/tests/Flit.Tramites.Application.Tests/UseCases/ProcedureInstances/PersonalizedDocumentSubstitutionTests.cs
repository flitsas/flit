using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11316 (Feature #11309, ADR-0042) — punto ÚNICO de sustitución por documento personalizado de
/// compañía en <see cref="GenerarFurHandler"/>. Cubre el oráculo de no regresión (CF-01/CF-03, sin
/// resolutor el comportamiento es idéntico al de antes de esta HU), la sustitución efectiva y trazable
/// (CF-02), la idempotencia de dos regeneraciones consecutivas (CF-09), la supervivencia del
/// personalizado a la rama de limpieza de <c>mandato</c> (DT-3) y el aislamiento del fallo (CF-12).
///
/// <para>La mecánica se ejercita con un DOBLE DE PRUEBA de <see cref="IPersonalizedDocumentResolver"/>:
/// en producción la lista de tipos habilitados está VACÍA hasta las HUs #11317/#11318
/// (<c>PersonalizedDocumentResolver.EnabledTypes</c>), así que estos tests no habilitan ningún tipo real.</para>
/// </summary>
public sealed class PersonalizedDocumentSubstitutionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IFurDocumentGenerator _generator = new MockFurDocumentGenerator();
    private readonly IKyverumCertificateClient _certClient = Substitute.For<IKyverumCertificateClient>();
    private readonly IRuesCertificateGenerator _ruesGenerator = Substitute.For<IRuesCertificateGenerator>();
    private readonly IRnmcCertificateGenerator _rnmcGenerator = Substitute.For<IRnmcCertificateGenerator>();
    private readonly IProcedureInstancePrendaRepository _prendaRepo = Substitute.For<IProcedureInstancePrendaRepository>();
    private readonly RecordingStorage _storage = new();
    private readonly FakeMandatoGenerator _mandatoGenerator = new();

    private static ProcedureInstance NewInstance()
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoMatriculaInicial ?? "matricula_inicial"),
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000099",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "matricula_inicial",
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoMatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = InstanceId,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });
        return instance;
    }

    private GenerarFurHandler NewHandler(
        IPersonalizedDocumentResolver? resolver = null,
        IMandatoGenerator? mandatoGenerator = null,
        ISolicitudVirtualGenerator? solicitudVirtualGenerator = null,
        ISignatureVaultPolicy? vaultPolicy = null) =>
        new(
            _repo,
            _generator,
            _certClient,
            _ruesGenerator,
            _rnmcGenerator,
            _prendaRepo,
            _storage,
            NullLogger<GenerarFurHandler>.Instance,
            vaultPolicy: vaultPolicy,
            solicitudVirtualGenerator: solicitudVirtualGenerator,
            mandatoGenerator: mandatoGenerator,
            personalizedDocumentResolver: resolver);

    // ---- dobles de prueba ---------------------------------------------------------------------

    private sealed class FakeMandatoGenerator : IMandatoGenerator
    {
        public GeneratedDocument GenerateMandato(MandatoData data) =>
            new("mandato", "mandato.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF MANDATO SISTEMA"));
    }

    /// <summary>Storage en memoria que calcula un SHA-256 real (para el oráculo de idempotencia).</summary>
    private sealed class RecordingStorage : IAttachmentStorage
    {
        public List<string> Deleted { get; } = [];
        public Dictionary<string, byte[]> SavedContentByPath { get; } = [];
        public List<(string Tipo, byte[] Content)> SavedCalls { get; } = [];

        /// <summary>
        /// HU #11318, AC2 — rutas "sembradas" para simular el artefacto REAL del baúl de firmas
        /// (<see cref="OpenReadAsync"/> las devuelve; cualquier otra ruta sigue devolviendo <c>null</c>,
        /// comportamiento previo intacto para el resto de las pruebas de esta clase).
        /// </summary>
        public Dictionary<string, byte[]> SeedForRead { get; } = [];

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            var path = $"{procedureInstanceId:D}/{tipo}_{SavedContentByPath.Count}";
            SavedContentByPath[path] = bytes;
            SavedCalls.Add((tipo, bytes));
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            return new StoredFile(path, sha, bytes.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Deleted.Add(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(
                SeedForRead.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    /// <summary>Resolutor de prueba: sustituye/omite exactamente lo que el test le configuró.</summary>
    private sealed class ScriptedResolver : IPersonalizedDocumentResolver
    {
        private readonly PersonalizedDocumentResolution _result;
        public List<string> RequestedTipos { get; } = [];

        public ScriptedResolver(PersonalizedDocumentResolution result) => _result = result;

        public Task<PersonalizedDocumentResolution> ResolveAsync(
            Guid tenantId, IEnumerable<string> tipos, CancellationToken ct = default)
        {
            RequestedTipos.AddRange(tipos);
            return Task.FromResult(_result);
        }
    }

    // ---- oráculo de no regresión (CF-01/CF-03) -------------------------------------------------

    [Fact]
    public async Task SinResolutor_ComportamientoIdenticoAlPrevio()
    {
        // Sin IPersonalizedDocumentResolver inyectado (NullPersonalizedDocumentResolver, default de
        // producción hasta #11317/#11318): el mandato se persiste EXACTAMENTE como antes de esta HU.
        var ct = TestContext.Current.CancellationToken;
        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, ct).Returns(instance);
        var handler = NewHandler(mandatoGenerator: _mandatoGenerator);

        var (result, error) = await handler.HandleAsync(InstanceId, TenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        var mandato = instance.Attachments.Single(a => a.Tipo == "mandato");
        mandato.Source.Should().Be("system");
        mandato.SourcePersonalizedDocumentId.Should().BeNull();
        _storage.SavedContentByPath[mandato.StoragePath]
            .Should().BeEquivalentTo(Encoding.UTF8.GetBytes("%PDF MANDATO SISTEMA"));
        instance.Events.Should().NotContain(e =>
            e.Tipo == "documento_personalizado_emitido" || e.Tipo == "documento_personalizado_no_disponible");
    }

    // ---- sustitución efectiva y trazable (CF-02, CF-03, CF-07 parcial) -------------------------

    [Fact]
    public async Task ConDobleDePrueba_SustituyeElMandatoYRegistraElEvento()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, ct).Returns(instance);

        var versionId = Guid.NewGuid();
        var contenidoPersonalizado = Encoding.UTF8.GetBytes("%PDF MANDATO DE LA COMPAÑÍA");
        var resolver = new ScriptedResolver(new PersonalizedDocumentResolution(
            [new ResolvedPersonalizedDocument("mandato", "mandato.pdf", contenidoPersonalizado, versionId, 3, "sha-declarado", 2)],
            []));
        var handler = NewHandler(resolver, _mandatoGenerator);

        var (result, error) = await handler.HandleAsync(InstanceId, TenantId, ct);

        error.Should().BeNull();
        resolver.RequestedTipos.Should().Contain("mandato");

        var mandato = instance.Attachments.Single(a => a.Tipo == "mandato");
        // CF-03 — el Tipo NO cambia: sigue siendo 'mandato' (heredado por construcción).
        mandato.Tipo.Should().Be("mandato");
        // CF-02 — sustituye y traza: Source='company' + referencia a la versión usada.
        mandato.Source.Should().Be("company");
        mandato.SourcePersonalizedDocumentId.Should().Be(versionId);
        // El contenido persistido es el de la COMPAÑÍA, no el del generador del sistema.
        _storage.SavedContentByPath[mandato.StoragePath].Should().BeEquivalentTo(contenidoPersonalizado);

        // Evento nuevo con la traza completa (tipo, id de versión, versión, sha256, páginas).
        var evento = instance.Events.Single(e => e.Tipo == "documento_personalizado_emitido");
        evento.Payload.Should().Contain("\"tipo\":\"mandato\"");
        evento.Payload.Should().Contain(versionId.ToString());
        evento.Payload.Should().Contain("\"version\":3");
        evento.Payload.Should().Contain("\"paginas\":2");
        instance.Events.Should().NotContain(e => e.Tipo == "documento_personalizado_no_disponible");
    }

    // ---- sustituye, no inyecta (CF-04) ---------------------------------------------------------

    [Fact]
    public async Task CuandoElDocumentoNoAplica_NoSeSustituyeNiSeCreaElAdjunto()
    {
        // Sin generador de mandato inyectado, TryGenerateMandatoAsync devuelve null: 'mandato' nunca
        // entra a `generated`. Aunque el resolutor (mal configurado a propósito) devolviera una
        // sustitución para 'mandato', no hay nada que sustituir.
        var ct = TestContext.Current.CancellationToken;
        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, ct).Returns(instance);

        var resolver = new ScriptedResolver(new PersonalizedDocumentResolution(
            [new ResolvedPersonalizedDocument("mandato", "mandato.pdf", "%PDF"u8.ToArray(), Guid.NewGuid(), 1, "sha", 1)],
            []));
        var handler = NewHandler(resolver, mandatoGenerator: null);

        var (_, error) = await handler.HandleAsync(InstanceId, TenantId, ct);

        error.Should().BeNull();
        instance.Attachments.Should().NotContain(a => a.Tipo == "mandato");
        resolver.RequestedTipos.Should().NotContain("mandato");
    }

    // ---- idempotencia (CF-09) -------------------------------------------------------------------

    [Fact]
    public async Task DosRegeneracionesConsecutivas_UnaSolaFilaMismoSha256YUnBorradoDelAnterior()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, ct).Returns(instance);

        var versionId = Guid.NewGuid();
        var contenidoPersonalizado = Encoding.UTF8.GetBytes("%PDF MANDATO DE LA COMPAÑÍA - V1");
        var resolver = new ScriptedResolver(new PersonalizedDocumentResolution(
            [new ResolvedPersonalizedDocument("mandato", "mandato.pdf", contenidoPersonalizado, versionId, 1, "sha-v1", 2)],
            []));
        var handler = NewHandler(resolver, _mandatoGenerator);

        var (_, error1) = await handler.HandleAsync(InstanceId, TenantId, ct);
        error1.Should().BeNull();
        var primeraRuta = instance.Attachments.Single(a => a.Tipo == "mandato").StoragePath;
        var primerSha = instance.Attachments.Single(a => a.Tipo == "mandato").Sha256;

        var (_, error2) = await handler.HandleAsync(InstanceId, TenantId, ct);
        error2.Should().BeNull();

        instance.Attachments.Where(a => a.Tipo == "mandato").Should().HaveCount(1);
        var segundaFila = instance.Attachments.Single(a => a.Tipo == "mandato");
        segundaFila.Source.Should().Be("company");
        segundaFila.Sha256.Should().Be(primerSha); // mismo contenido ⇒ mismo hash en ambas regeneraciones.
        // El FUR también se regenera cada vez (idempotencia general); lo que importa para CF-09 es que
        // la ruta del MANDATO de la primera regeneración se borró EXACTAMENTE una vez.
        _storage.Deleted.Count(p => p == primeraRuta).Should().Be(1);
    }

    // ---- DT-3: el personalizado sobrevive a la limpieza de mandato -----------------------------

    [Fact]
    public async Task CuandoElMandatoDejaDeAplicar_ElPersonalizadoSobrevive()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, ct).Returns(instance);

        var versionId = Guid.NewGuid();
        var resolver = new ScriptedResolver(new PersonalizedDocumentResolution(
            [new ResolvedPersonalizedDocument("mandato", "mandato.pdf", "%PDF COMPAÑÍA"u8.ToArray(), versionId, 1, "sha", 1)],
            []));

        // Primera generación: el mandato SÍ aplica y queda sustituido (Source='company').
        var handlerConMandato = NewHandler(resolver, _mandatoGenerator);
        await handlerConMandato.HandleAsync(InstanceId, TenantId, ct);
        var mandatoPersonalizado = instance.Attachments.Single(a => a.Tipo == "mandato");
        mandatoPersonalizado.Source.Should().Be("company");
        var rutaPersonalizado = mandatoPersonalizado.StoragePath;

        // Segunda generación: el mandato DEJA de aplicar (sin generador). La rama de limpieza
        // (guardada por AttachmentCleanup) NO debe destruir el documento personalizado ni su archivo.
        var handlerSinMandato = NewHandler(resolver, mandatoGenerator: null);
        var (_, error) = await handlerSinMandato.HandleAsync(InstanceId, TenantId, ct);

        error.Should().BeNull();
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "mandato" && a.Source == "company");
        _storage.Deleted.Should().NotContain(rutaPersonalizado);
    }

    // ---- aislamiento del fallo (CF-12) -----------------------------------------------------------

    [Fact]
    public async Task CuandoElPersonalizadoNoSePuedeLeer_CaeAlSistemaYRegistraElEventoSinPii()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, ct).Returns(instance);

        var versionId = Guid.NewGuid();
        var resolver = new ScriptedResolver(new PersonalizedDocumentResolution(
            [],
            [new PersonalizedDocumentUnavailable("mandato", versionId, "pdf_ilegible")]));
        var handler = NewHandler(resolver, _mandatoGenerator);

        var (result, error) = await handler.HandleAsync(InstanceId, TenantId, ct);

        // Nunca un 500 / nunca falla la generación: el expediente se genera igual, con el documento
        // del sistema.
        error.Should().BeNull();
        result.Should().NotBeNull();
        var mandato = instance.Attachments.Single(a => a.Tipo == "mandato");
        mandato.Source.Should().Be("system");
        mandato.SourcePersonalizedDocumentId.Should().BeNull();

        var evento = instance.Events.Single(e => e.Tipo == "documento_personalizado_no_disponible");
        evento.Payload.Should().Contain("\"tipo\":\"mandato\"");
        evento.Payload.Should().Contain("\"motivo\":\"pdf_ilegible\"");
        evento.Payload.Should().Contain(versionId.ToString());
        // Sin datos personales: ni nombre ni documento de ninguna parte en el payload.
        evento.Payload.Should().NotContain("comprador");
        instance.Events.Should().NotContain(e => e.Tipo == "documento_personalizado_emitido");
    }

    // ---- HU #11317 — el mandato personalizado NO lleva bloques de firma del mandatario ---------

    [Fact]
    public async Task ElMandatoPersonalizado_NoLlevaBloquesDeFirmaDelMandatario()
    {
        // El PDF de la compañía es un reemplazo ESTÁTICO (ADR-0042 §Decisión, restricción del PO): el
        // contenido persistido es EXACTAMENTE el que devolvió el resolutor, byte a byte — el pipeline
        // no le inyecta ni estampa ningún bloque de firma antes de guardarlo.
        var ct = TestContext.Current.CancellationToken;
        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, ct).Returns(instance);

        var contenidoPersonalizado = Encoding.UTF8.GetBytes("%PDF MANDATO SIN BLOQUES DE FIRMA");
        var resolver = new ScriptedResolver(new PersonalizedDocumentResolution(
            [new ResolvedPersonalizedDocument("mandato", "mandato.pdf", contenidoPersonalizado, Guid.NewGuid(), 1, "sha", 1)],
            []));
        var handler = NewHandler(resolver, _mandatoGenerator);

        var (_, error) = await handler.HandleAsync(InstanceId, TenantId, ct);

        error.Should().BeNull();
        var mandato = instance.Attachments.Single(a => a.Tipo == "mandato");
        // Byte a byte: ni un sello del baúl, ni una marca de identidad, ni ningún dato del trámite
        // estampado — es el mismo arreglo de bytes que subió la compañía.
        _storage.SavedContentByPath[mandato.StoragePath].Should().BeEquivalentTo(contenidoPersonalizado);
    }

    // ---- HU #11318, AC1 — tramite_virtual se sustituye en TODOS los trámites (PN y PJ) -----------

    private static ProcedureInstance NewTraspasoInstance()
    {
        var id = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard ?? "traspaso"),
            Id = id,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000199",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "traspaso",
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoTraspasoStandard,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });
        return instance;
    }

    /// <summary>Genera la 'solicitud de trámite virtual' del sistema — ADR-0036 (HU #10914), siempre.</summary>
    private sealed class FakeSolicitudVirtualGenerator : ISolicitudVirtualGenerator
    {
        public GeneratedDocument GenerateSolicitudVirtual(FurDocumentData data) =>
            new("tramite_virtual", "solicitud_tramite_virtual.pdf", "application/pdf",
                Encoding.UTF8.GetBytes("%PDF SOLICITUD DE TRAMITE VIRTUAL — SISTEMA"));
    }

    private static ProcedureInstanceActor ActorNaturalComprador(ProcedureInstance instance) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = instance.TenantId,
        ProcedureInstanceId = instance.Id,
        ProcedureEntityId = Guid.NewGuid(),
        ActorType = "comprador",
        DocumentType = "CC",
        DocumentNumber = "1000222333",
        FullName = "PERSONA NATURAL DEMO",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ProcedureInstanceActor ActorJuridicoVendedor(ProcedureInstance instance) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = instance.TenantId,
        ProcedureInstanceId = instance.Id,
        ProcedureEntityId = Guid.NewGuid(),
        ActorType = "vendedor",
        DocumentType = "NIT",
        DocumentNumber = "900333444",
        FullName = "EMPRESA VENDEDORA S.A.S.",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task AC1_PersonaNatural_TramiteVirtual_SeSustituyeYOcupaLaMismaPosicion()
    {
        // AC1 — un trámite de PERSONA NATURAL (matrícula, comprador con cédula): la solicitud
        // personalizada sustituye a la del sistema y ocupa la posición del tipo 'tramite_virtual'
        // (mismo Tipo, mismo lugar en el expediente — DT-2/CF-03). El resto (FUR) NO se toca.
        var ct = TestContext.Current.CancellationToken;
        var instance = NewInstance();
        instance.Actors.Add(ActorNaturalComprador(instance));
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, ct).Returns(instance);

        var versionId = Guid.NewGuid();
        var contenidoPersonalizado = Encoding.UTF8.GetBytes("%PDF SOLICITUD DE LA COMPAÑÍA — PN");
        var resolver = new ScriptedResolver(new PersonalizedDocumentResolution(
            [new ResolvedPersonalizedDocument("tramite_virtual", "solicitud.pdf", contenidoPersonalizado, versionId, 2, "sha", 1)],
            []));
        var handler = NewHandler(resolver, solicitudVirtualGenerator: new FakeSolicitudVirtualGenerator());

        var (_, error) = await handler.HandleAsync(InstanceId, TenantId, ct);

        error.Should().BeNull();
        resolver.RequestedTipos.Should().Contain("tramite_virtual");

        var tramiteVirtual = instance.Attachments.Single(a => a.Tipo == "tramite_virtual");
        tramiteVirtual.Tipo.Should().Be("tramite_virtual"); // conserva el Tipo ⇒ misma posición en el expediente
        tramiteVirtual.Source.Should().Be("company");
        tramiteVirtual.SourcePersonalizedDocumentId.Should().Be(versionId);
        _storage.SavedContentByPath[tramiteVirtual.StoragePath].Should().BeEquivalentTo(contenidoPersonalizado);

        // El FUR (único otro documento de esta instancia) conserva su Source del sistema, intacto.
        var fur = instance.Attachments.Single(a => a.Tipo == "fur");
        fur.Source.Should().Be("system");
    }

    [Fact]
    public async Task AC1_PersonaJuridica_TramiteVirtual_SeSustituyeIgualQueEnPersonaNatural()
    {
        // AC1 — mismo mecanismo en un trámite de PERSONA JURÍDICA (traspaso, vendedor con NIT): el
        // radio de impacto de esta HU es "todos los trámites", no solo personas naturales.
        var ct = TestContext.Current.CancellationToken;
        var instance = NewTraspasoInstance();
        instance.Actors.Add(ActorNaturalComprador(instance));
        instance.Actors.Add(ActorJuridicoVendedor(instance));
        _repo.GetByIdWithFurGraphAsync(instance.Id, TenantId, ct).Returns(instance);

        var versionId = Guid.NewGuid();
        var contenidoPersonalizado = Encoding.UTF8.GetBytes("%PDF SOLICITUD DE LA COMPAÑÍA — PJ");
        var resolver = new ScriptedResolver(new PersonalizedDocumentResolution(
            [new ResolvedPersonalizedDocument("tramite_virtual", "solicitud.pdf", contenidoPersonalizado, versionId, 5, "sha", 1)],
            []));
        var handler = NewHandler(resolver, solicitudVirtualGenerator: new FakeSolicitudVirtualGenerator());

        var (_, error) = await handler.HandleAsync(instance.Id, TenantId, ct);

        error.Should().BeNull();
        var tramiteVirtual = instance.Attachments.Single(a => a.Tipo == "tramite_virtual");
        tramiteVirtual.Tipo.Should().Be("tramite_virtual");
        tramiteVirtual.Source.Should().Be("company");
        tramiteVirtual.SourcePersonalizedDocumentId.Should().Be(versionId);
        _storage.SavedContentByPath[tramiteVirtual.StoragePath].Should().BeEquivalentTo(contenidoPersonalizado);

        // El FUR y la compraventa (traspaso siempre los genera, ADR-0035) conservan Source='system':
        // no son personalizables (AC4), y no cambian de comportamiento por convivir con la sustitución.
        instance.Attachments.Single(a => a.Tipo == "fur").Source.Should().Be("system");
        instance.Attachments.Single(a => a.Tipo == "compraventa").Source.Should().Be("system");
    }

    // ---- HU #11318, AC2 — el personalizado pierde el sello del baúl; FUR y compraventa lo conservan ----

    /// <summary>
    /// Actor jurídico con representante legal SIN elección explícita de mecanismo: manda la precedencia
    /// del baúl (HU #11031) — es el actor de <see cref="FirmaBaulCobertura.Aplica"/>.
    /// </summary>
    private static ProcedureInstanceActor ActorJuridicoConBaul(ProcedureInstance instance, string rol) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = instance.TenantId,
        ProcedureInstanceId = instance.Id,
        ProcedureEntityId = Guid.NewGuid(),
        ActorType = rol,
        DocumentType = "NIT",
        DocumentNumber = "900555666",
        FullName = "COMPRADORA S.A.S.",
        PersonType = "juridical",
        Metadata = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["representanteLegal"] = new Dictionary<string, object?>
            {
                ["tipoDocumento"] = "CC",
                ["numeroDocumento"] = "52082029",
                ["nombreCompleto"] = "REPRESENTANTE DEMO",
                ["mecanismoFirma"] = null, // sin elección explícita ⇒ precedencia del baúl
            },
        }),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Baúl que siempre resuelve firma vigente para la persona consultada (mismo doble que FurHandlerTests).</summary>
    private sealed class VaultConFirma : ISignatureVaultPolicy
    {
        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default) =>
            Task.FromResult<SignatureVaultMatch?>(new SignatureVaultMatch(
                Guid.NewGuid(), "REPRESENTANTE DEMO", "hash", "vault/comprador.png", "sha-vault",
                new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "52082029"));
    }

    /// <summary>
    /// Generador que ESTAMPA en el contenido si <c>FirmaBaulMetadatos</c> llegó poblado — así el test
    /// puede probar por CONTENIDO (no solo por dato assemblado) si el sello del baúl sobrevive o no en
    /// cada documento final persistido.
    /// </summary>
    private sealed class SelloEstampandoGenerator : IFurDocumentGenerator, ISolicitudVirtualGenerator
    {
        public GeneratedDocument GenerateFur(FurDocumentData data) => Doc("fur", data);
        public GeneratedDocument GenerateCompraventa(FurDocumentData data) => Doc("compraventa", data);
        public GeneratedDocument GenerateSolicitudVirtual(FurDocumentData data) => Doc("tramite_virtual", data);

        private static GeneratedDocument Doc(string tipo, FurDocumentData data)
        {
            var conSello = data.FirmaBaulMetadatos?.ContainsKey("comprador") == true;
            var marcador = conSello ? "SELLO_BAUL:presente" : "SELLO_BAUL:ausente";
            return new GeneratedDocument(tipo, $"{tipo}.pdf", "application/pdf",
                Encoding.UTF8.GetBytes($"%PDF {tipo} {marcador}"));
        }
    }

    [Fact]
    public async Task AC2_ElTramiteVirtualPersonalizado_PierdeElSelloDelBaul_MientrasFurYCompraventaLoConservan()
    {
        // Riesgo aceptado por el PO (R-4): la solicitud personalizada pierde la vigencia+hash del baúl
        // porque su contenido se sustituye ENTERO por el PDF de la compañía (DT-5, sin inyectar ningún
        // dato del trámite). El resto del expediente —FUR y compraventa, ninguno personalizable— sigue
        // recibiendo el mismo `FurDocumentData` con el sello poblado y lo estampa igual que siempre.
        var ct = TestContext.Current.CancellationToken;
        var instance = NewTraspasoInstance();
        var comprador = ActorJuridicoConBaul(instance, "comprador");
        instance.Actors.Add(comprador);
        var vendedor = ActorNaturalComprador(instance);
        vendedor.ActorType = "vendedor";
        instance.Actors.Add(vendedor);
        _repo.GetByIdWithFurGraphAsync(instance.Id, TenantId, ct).Returns(instance);

        var storageConBaul = new RecordingStorage();
        storageConBaul.SeedForRead["vault/comprador.png"] = "PNG-FIRMA-REAL"u8.ToArray();
        var generadorConSello = new SelloEstampandoGenerator();

        var versionId = Guid.NewGuid();
        var contenidoPersonalizado = Encoding.UTF8.GetBytes("%PDF SOLICITUD DE LA COMPAÑÍA — SIN SELLO");
        var resolver = new ScriptedResolver(new PersonalizedDocumentResolution(
            [new ResolvedPersonalizedDocument("tramite_virtual", "solicitud.pdf", contenidoPersonalizado, versionId, 1, "sha", 1)],
            []));

        var handler = new GenerarFurHandler(
            _repo, generadorConSello, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo,
            storageConBaul, NullLogger<GenerarFurHandler>.Instance,
            vaultPolicy: new VaultConFirma(),
            solicitudVirtualGenerator: generadorConSello,
            personalizedDocumentResolver: resolver);

        var (_, error) = await handler.HandleAsync(instance.Id, TenantId, ct);

        error.Should().BeNull();

        // El sello del baúl SÍ se resolvió para este trámite (si no, esta prueba no probaría nada: FUR
        // y compraventa no tendrían nada que conservar).
        var fur = instance.Attachments.Single(a => a.Tipo == "fur");
        var compraventa = instance.Attachments.Single(a => a.Tipo == "compraventa");
        Encoding.UTF8.GetString(storageConBaul.SavedContentByPath[fur.StoragePath])
            .Should().Contain("SELLO_BAUL:presente", "el FUR no es personalizable (AC4): conserva el sello");
        Encoding.UTF8.GetString(storageConBaul.SavedContentByPath[compraventa.StoragePath])
            .Should().Contain("SELLO_BAUL:presente", "la compraventa no es personalizable (AC4): conserva el sello");

        // La solicitud de trámite virtual, en cambio, es EXACTAMENTE el PDF de la compañía — ni rastro
        // del marcador de sello que el generador del sistema hubiera estampado.
        var tramiteVirtual = instance.Attachments.Single(a => a.Tipo == "tramite_virtual");
        tramiteVirtual.Source.Should().Be("company");
        var contenidoGuardado = storageConBaul.SavedContentByPath[tramiteVirtual.StoragePath];
        contenidoGuardado.Should().BeEquivalentTo(contenidoPersonalizado);
        Encoding.UTF8.GetString(contenidoGuardado).Should().NotContain("SELLO_BAUL");
    }

    // ---- HU #11317 — volver al documento del sistema restaura el mandato de FLIT ----------------

    [Fact]
    public async Task CuandoLaCompaniaVuelveAlSistema_LaSiguienteRegeneracionRestauraElMandatoDeFlit()
    {
        // Primera regeneración: la compañía tiene un mandato personalizado activo ⇒ se sustituye.
        var ct = TestContext.Current.CancellationToken;
        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, ct).Returns(instance);

        var versionId = Guid.NewGuid();
        var resolverConPersonalizado = new ScriptedResolver(new PersonalizedDocumentResolution(
            [new ResolvedPersonalizedDocument("mandato", "mandato.pdf", "%PDF COMPAÑÍA"u8.ToArray(), versionId, 1, "sha", 1)],
            []));
        var handlerConPersonalizado = NewHandler(resolverConPersonalizado, _mandatoGenerator);
        await handlerConPersonalizado.HandleAsync(InstanceId, TenantId, ct);

        instance.Attachments.Single(a => a.Tipo == "mandato").Source.Should().Be("company");

        // La compañía "vuelve al documento del sistema" (HU #11314 — desactiva su versión): el
        // resolutor ya no tiene nada que ofrecer para 'mandato' en la SIGUIENTE regeneración.
        var resolverSinPersonalizado = new ScriptedResolver(PersonalizedDocumentResolution.Empty);
        var handlerSinPersonalizado = NewHandler(resolverSinPersonalizado, _mandatoGenerator);

        var (_, error) = await handlerSinPersonalizado.HandleAsync(InstanceId, TenantId, ct);

        error.Should().BeNull();
        var mandato = instance.Attachments.Single(a => a.Tipo == "mandato");
        // El mandato de FLIT queda restaurado: Source vuelve a 'system' y el contenido es el del
        // generador (no queda ningún rastro del PDF de la compañía).
        mandato.Source.Should().Be("system");
        mandato.SourcePersonalizedDocumentId.Should().BeNull();
        _storage.SavedContentByPath[mandato.StoragePath]
            .Should().BeEquivalentTo(Encoding.UTF8.GetBytes("%PDF MANDATO SISTEMA"));
    }
}

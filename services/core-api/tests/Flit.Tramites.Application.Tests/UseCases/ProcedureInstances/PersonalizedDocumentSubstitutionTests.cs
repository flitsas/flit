using System.Security.Cryptography;
using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
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
        IMandatoGenerator? mandatoGenerator = null) =>
        new(
            _repo,
            _generator,
            _certClient,
            _ruesGenerator,
            _rnmcGenerator,
            _prendaRepo,
            _storage,
            NullLogger<GenerarFurHandler>.Instance,
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
            Task.FromResult<Stream?>(null);

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
}

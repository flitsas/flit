using System.Text;
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
/// Bug reportado en DEV: el resumen mostraba a "Carlos Pérez Demo" marcado como MANDATARIO en pantalla,
/// pero el documento del mandato salía firmado por OTRA persona (o sin firmante).
///
/// <para>Este test fija EXACTAMENTE ese escenario: con un default del OT configurado (módulo Mandatos) y
/// SIN elección explícita del gestor (<c>instance.MandateSignerId</c> nulo), lo que pinta el listado de
/// pantalla (<see cref="ListMandateSignerOptionsHandler"/>) y lo que firma el documento
/// (<see cref="GenerarFurHandler"/> → <c>TryGenerateMandatoAsync</c>) deben coincidir en el MISMO
/// mandatario.</para>
///
/// <para><b>Antes de la corrección este test FALLABA:</b> la pantalla sugería el default parametrizado
/// (cascada completa: elegido → default del OT → único candidato), mientras el documento recibía
/// <c>instance.MandateSignerId</c> CRUDO (null, sin cascada) y generaba el mandato SIN firmante
/// (<c>Mandatario</c> null) — comprobado ejecutando este test contra el código previo a la corrección.</para>
/// </summary>
public sealed class MandateSignerBugReproTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid Ana = Guid.Parse("cccccccc-2222-4000-8000-000000000001");
    private static readonly Guid Carlos = Guid.Parse("cccccccc-2222-4000-8000-000000000002");

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    /// <summary>Directorio con los dos mandatarios habilitados para el OT/compañía del test.</summary>
    private sealed class Directorio(params MandateSignerCandidate[] candidatos) : IMandateSignerDirectory
    {
        public Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
            Guid transitOfficeId, Guid companyTenantId, string? nitMandante = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MandateSignerCandidate>>(
                transitOfficeId == Ot && companyTenantId == TenantId ? candidatos : []);

        public Task<MandateSignerCandidate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(candidatos.FirstOrDefault(c => c.Id == id));
    }

    /// <summary>Captura el <see cref="MandatoData"/> con el que se generó el mandato para inspeccionarlo.</summary>
    private sealed class CapturingMandatoGenerator : IMandatoGenerator
    {
        public MandatoData? Captured { get; private set; }

        public GeneratedDocument GenerateMandato(MandatoData data)
        {
            Captured = data;
            return new GeneratedDocument(
                "mandato", "mandato.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF MANDATO"));
        }
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content,
            CancellationToken ct = default) =>
            Task.FromResult(new StoredFile($"{procedureInstanceId:D}/{tipo}", $"sha-{tipo}", 10));

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath)
        {
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    /// <summary>
    /// Misma "foto" de trámite para pantalla y documento: matrícula inicial, borrador, mismo OT (columna
    /// TransitOfficeId, como queda tras radicar) y SIN elección explícita de mandatario.
    /// </summary>
    private static ProcedureInstance NewInstance() => new()
    {
        Id = InstanceId,
        TenantId = TenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000777",
        Status = TramiteEstado.Borrador,
        ModalidadEntrada = "matricula_inicial",
        TipologiaCodigo = TramiteTipologiaCatalog.CodigoMatriculaInicial,
        TransitOfficeId = Ot,
        MandateSignerId = null,
        CreatedAt = DateTimeOffset.UtcNow,
        FieldValues =
        {
            new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProcedureInstanceId = InstanceId,
                FieldKey = "transit_office_code",
                ValueText = "11001000",
                Source = "user",
            },
        },
    };

    private static MandateOtConfig DefaultCarlosConfig() => new(
        Ot,
        "generico",
        RequiresForNaturalPerson: false,
        InstitutionalMandataryName: null,
        InstitutionalMandataryNit: null,
        AssignmentMode: "signer",
        DefaultMandateSignerId: Carlos);

    [Fact]
    public async Task Pantalla_y_Documento_ResuelvenElMismoMandatario_ConDefaultDelOtYSinEleccion()
    {
        var ct = TestContext.Current.CancellationToken;

        var directorio = new Directorio(
            new MandateSignerCandidate(Ana, "Ana Restrepo", "111000111", null),
            new MandateSignerCandidate(Carlos, "Carlos Pérez Demo", "222000222", null));

        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveAsync("11001000", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(DefaultCarlosConfig());

        // ---- 1) Lo que se muestra en pantalla ----------------------------------------------------
        var pantallaInstance = NewInstance();
        _repo.GetByIdWithDetailsAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(pantallaInstance);

        var pantalla = new ListMandateSignerOptionsHandler(_repo, directorio, mandatePolicy: policy);
        var (resultPantalla, errorPantalla) = await pantalla.HandleAsync(InstanceId, TenantId, ct);

        errorPantalla.Should().BeNull();
        resultPantalla!.ElegidoId.Should().Be(
            Carlos, "el default parametrizado del OT (Carlos) se sugiere en pantalla sin elección explícita");

        // ---- 2) Lo que usa el documento -----------------------------------------------------------
        var documentoInstance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(documentoInstance);

        var mandatoGenerator = new CapturingMandatoGenerator();
        var furHandler = new GenerarFurHandler(
            _repo,
            new MockFurDocumentGenerator(),
            Substitute.For<IKyverumCertificateClient>(),
            Substitute.For<IRuesCertificateGenerator>(),
            Substitute.For<IRnmcCertificateGenerator>(),
            Substitute.For<IProcedureInstancePrendaRepository>(),
            new FakeStorage(),
            NullLogger<GenerarFurHandler>.Instance,
            mandatoGenerator: mandatoGenerator,
            mandatePolicy: policy,
            mandateDirectory: directorio);

        var (resultDoc, errorDoc) = await furHandler.HandleAsync(InstanceId, TenantId, ct);

        errorDoc.Should().BeNull();
        resultDoc.Should().NotBeNull();
        mandatoGenerator.Captured.Should().NotBeNull();

        // El bug reportado, fijado en una aserción: el documento NO debe quedar sin firmante (ni con el
        // firmante equivocado) cuando la pantalla ya está sugiriendo uno por el default del OT.
        mandatoGenerator.Captured!.Mandatario.Should().NotBeNull(
            "el documento debe llevar firmante cuando hay un default del OT, igual que sugiere la pantalla");
        mandatoGenerator.Captured.Mandatario!.Documento.Should().Be(
            "222000222",
            "el mandato debe firmarlo el MISMO mandatario (Carlos) que el listado de pantalla marca como elegido");

        // ---- 3) La instancia queda con la resolución REGISTRADA (no recalculada cada vez) --------
        documentoInstance.MandateSignerId.Should().Be(
            Carlos, "el mandato es un documento legal: quién lo firmó debe quedar persistido, no recalculado");
    }

    private GenerarFurHandler NewFurHandler(
        IMandateSignerDirectory directorio,
        IMandateRequirementPolicy? policy,
        CapturingMandatoGenerator mandatoGenerator) =>
        new(
            _repo,
            new MockFurDocumentGenerator(),
            Substitute.For<IKyverumCertificateClient>(),
            Substitute.For<IRuesCertificateGenerator>(),
            Substitute.For<IRnmcCertificateGenerator>(),
            Substitute.For<IProcedureInstancePrendaRepository>(),
            new FakeStorage(),
            NullLogger<GenerarFurHandler>.Instance,
            mandatoGenerator: mandatoGenerator,
            mandatePolicy: policy,
            mandateDirectory: directorio);

    [Fact]
    public async Task SinDefaultYVariosCandidatos_ElDocumentoQuedaSinFirmante_YNoPersisteNada()
    {
        // Documentado en MandateSignerDefaultResolverTests: varios candidatos sin default no arriesgan
        // una sugerencia. El documento debe comportarse igual que hoy (placeholders, sin firmante), y
        // NO debe escribir instance.MandateSignerId con un candidato al azar.
        var ct = TestContext.Current.CancellationToken;
        var directorio = new Directorio(
            new MandateSignerCandidate(Ana, "Ana Restrepo", "111000111", null),
            new MandateSignerCandidate(Carlos, "Carlos Pérez Demo", "222000222", null));

        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveAsync("11001000", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(
                Ot, "generico", RequiresForNaturalPerson: false, null, null,
                AssignmentMode: "signer", DefaultMandateSignerId: null));

        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var mandatoGenerator = new CapturingMandatoGenerator();
        var handler = NewFurHandler(directorio, policy, mandatoGenerator);

        var (result, error) = await handler.HandleAsync(InstanceId, TenantId, ct);

        error.Should().BeNull();
        mandatoGenerator.Captured!.Mandatario.Should().BeNull();
        instance.MandateSignerId.Should().BeNull();
    }

    [Fact]
    public async Task EleccionExplicitaYaGuardada_MandaSobreElDefaultDelOt_EnElDocumento()
    {
        var ct = TestContext.Current.CancellationToken;
        var directorio = new Directorio(
            new MandateSignerCandidate(Ana, "Ana Restrepo", "111000111", null),
            new MandateSignerCandidate(Carlos, "Carlos Pérez Demo", "222000222", null));

        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveAsync("11001000", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(DefaultCarlosConfig()); // default = Carlos

        var instance = NewInstance();
        instance.MandateSignerId = Ana; // elección explícita del gestor, distinta del default
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var mandatoGenerator = new CapturingMandatoGenerator();
        var handler = NewFurHandler(directorio, policy, mandatoGenerator);

        var (result, error) = await handler.HandleAsync(InstanceId, TenantId, ct);

        error.Should().BeNull();
        mandatoGenerator.Captured!.Mandatario!.Documento.Should().Be("111000111", "la elección del gestor manda sobre el default");
        instance.MandateSignerId.Should().Be(Ana, "una elección ya guardada no se reescribe");
    }

    [Theory]
    [InlineData("institutional")]
    [InlineData("open")]
    public async Task ModosSinFirmantePersona_NoAsignanMandatario_NiPersistenNada(string assignmentMode)
    {
        var ct = TestContext.Current.CancellationToken;
        var directorio = new Directorio(
            new MandateSignerCandidate(Ana, "Ana Restrepo", "111000111", null),
            new MandateSignerCandidate(Carlos, "Carlos Pérez Demo", "222000222", null));

        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveAsync("11001000", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(
                Ot, "generico", RequiresForNaturalPerson: false,
                InstitutionalMandataryName: "UT-Demo", InstitutionalMandataryNit: "900000000-1",
                AssignmentMode: assignmentMode, DefaultMandateSignerId: Carlos));

        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var mandatoGenerator = new CapturingMandatoGenerator();
        var handler = NewFurHandler(directorio, policy, mandatoGenerator);

        var (result, error) = await handler.HandleAsync(InstanceId, TenantId, ct);

        error.Should().BeNull();
        mandatoGenerator.Captured!.Mandatario.Should().BeNull(
            "institucional/abierto no llevan firmante persona, aunque haya default y candidatos");
        instance.MandateSignerId.Should().BeNull("estos modos no fijan ni persisten mandatario persona");
    }

    [Fact]
    public async Task DosRegeneracionesConsecutivas_SonIdempotentes_MismoFirmanteSinReescrituraExtra()
    {
        var ct = TestContext.Current.CancellationToken;
        var directorio = new Directorio(
            new MandateSignerCandidate(Ana, "Ana Restrepo", "111000111", null),
            new MandateSignerCandidate(Carlos, "Carlos Pérez Demo", "222000222", null));

        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveAsync("11001000", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(DefaultCarlosConfig());

        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var mandatoGenerator = new CapturingMandatoGenerator();
        var handler = NewFurHandler(directorio, policy, mandatoGenerator);

        var (_, error1) = await handler.HandleAsync(InstanceId, TenantId, ct);
        error1.Should().BeNull();
        instance.MandateSignerId.Should().Be(Carlos);

        var (_, error2) = await handler.HandleAsync(InstanceId, TenantId, ct);
        error2.Should().BeNull();

        // Segunda regeneración: ya hay elección "guardada" (Carlos), así que el resolvedor la toma como
        // explícita y no la recalcula ni la cambia.
        instance.MandateSignerId.Should().Be(Carlos);
        mandatoGenerator.Captured!.Mandatario!.Documento.Should().Be("222000222");
    }
}

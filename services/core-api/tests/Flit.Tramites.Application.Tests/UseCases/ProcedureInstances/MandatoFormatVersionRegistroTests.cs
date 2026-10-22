using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Documents;
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
/// HU #13172 (Feature #13118, Épica #13090) — el contrato de mandato se genera con la plantilla publicada del formato y
/// registra la versión usada; regenerarlo reproduce esa versión y publicar otra no altera lo ya emitido.
/// Uso de ejemplo: con la versión 2 vigente se emite el mandato (adjunto con versión 2); el Super Admin publica la 3;
/// al regenerar el expediente el mandato vuelve a salir con la 2.
/// </summary>
public sealed class MandatoFormatVersionRegistroTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid Ot = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    /// <summary>Catálogo de plantillas en memoria con la misma semántica que el proveedor real (vigente o fijada).</summary>
    private sealed class FakeProvider : IMandateFormatTemplateProvider
    {
        public Dictionary<int, string> Versions { get; } = [];
        public int Current { get; set; }
        public List<int?> PinnedRequests { get; } = [];

        public Task<MandateFormatTemplate> ResolveAsync(string formatCode, int? pinnedVersion, CancellationToken ct = default)
        {
            PinnedRequests.Add(pinnedVersion);
            if (pinnedVersion is { } p)
            {
                if (p <= 0)
                    return Task.FromResult(new MandateFormatTemplate(formatCode, 0, null));
                if (Versions.TryGetValue(p, out var pinned))
                    return Task.FromResult(new MandateFormatTemplate(formatCode, p, pinned));
            }

            return Task.FromResult(Current > 0
                ? new MandateFormatTemplate(formatCode, Current, Versions[Current])
                : new MandateFormatTemplate(formatCode, 0, null));
        }
    }

    private sealed class CapturingMandatoGenerator : IMandatoGenerator
    {
        public MandatoData? Captured { get; private set; }

        public GeneratedDocument GenerateMandato(MandatoData data)
        {
            Captured = data;
            return new GeneratedDocument("mandato", "mandato.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF MANDATO"));
        }
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
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

    private static ProcedureInstance NewInstance() => new()
    {
        ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoMatriculaInicial ?? "matricula_inicial"),
        Id = InstanceId,
        TenantId = TenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-001300",
        Status = TramiteEstado.Borrador,
        TransitOfficeId = Ot,
        CreatedAt = DateTimeOffset.UtcNow,
        FieldValues =
        {
            new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ProcedureInstanceId = InstanceId,
                FieldKey = "transit_office_code", ValueText = "11001000", Source = "user",
            },
        },
    };

    private static MandateOtConfig Config(string? customKind = null, string? customBody = null) => new(
        Ot, "generico", RequiresForNaturalPerson: false, InstitutionalMandataryName: null,
        InstitutionalMandataryNit: null, AssignmentMode: "open", CustomTemplateKind: customKind,
        CustomTemplateBody: customBody);

    private (GenerarFurHandler Handler, CapturingMandatoGenerator Generator, ProcedureInstance Instance) Build(
        FakeProvider provider, MandateOtConfig config)
    {
        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveByOfficeIdAsync(Ot, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(config);
        var generator = new CapturingMandatoGenerator();
        var handler = new GenerarFurHandler(
            _repo,
            new MockFurDocumentGenerator(),
            Substitute.For<IKyverumCertificateClient>(),
            Substitute.For<IRuesCertificateGenerator>(),
            Substitute.For<IRnmcCertificateGenerator>(),
            Substitute.For<IProcedureInstancePrendaRepository>(),
            new FakeStorage(),
            NullLogger<GenerarFurHandler>.Instance,
            mandatoGenerator: generator,
            mandatePolicy: policy,
            mandateFormatTemplates: provider);
        return (handler, generator, instance);
    }

    private static ProcedureInstanceAttachment Mandato(ProcedureInstance i) =>
        i.Attachments.Single(a => a.Tipo == "mandato");

    [Fact]
    public async Task AC1_TramiteNuevo_UsaLaVersionVigente_ConLasVariables_YRegistraLaVersionUsada()
    {
        var provider = new FakeProvider { Current = 2 };
        provider.Versions[1] = "Texto uno";
        provider.Versions[2] = "Contrato sobre {{placa}} de {{mandante_nombre}}";
        var (handler, generator, instance) = Build(provider, Config());

        var (_, error) = await handler.HandleAsync(InstanceId, TenantId, Ct);

        error.Should().BeNull();
        generator.Captured!.CustomTemplateKind.Should().Be(MandatoCustomTemplateKindCodes.Editor);
        generator.Captured.CustomTemplateBody.Should().Be("Contrato sobre {{placa}} de {{mandante_nombre}}");
        var adjunto = Mandato(instance);
        adjunto.MandateFormatVersion.Should().Be(2);
        adjunto.MandateFormatCode.Should().Be("generico");
    }

    [Fact]
    public async Task AC2_SinPersonalizacion_SeUsaLaRedaccionDelGenerador_Version0()
    {
        var provider = new FakeProvider();
        var (handler, generator, instance) = Build(provider, Config());

        var (_, error) = await handler.HandleAsync(InstanceId, TenantId, Ct);

        error.Should().BeNull();
        generator.Captured!.CustomTemplateKind.Should().Be(MandatoCustomTemplateKindCodes.None);
        generator.Captured.CustomTemplateBody.Should().BeNull();
        Mandato(instance).MandateFormatVersion.Should().Be(0);
    }

    [Fact]
    public async Task AC3_ContratoYaEmitidoIntacto_YAC4_LaRegeneracionReproduceLaVersionRegistrada()
    {
        var provider = new FakeProvider { Current = 2 };
        provider.Versions[2] = "Versión dos {{placa}}";
        var (handler, generator, instance) = Build(provider, Config());
        (await handler.HandleAsync(InstanceId, TenantId, Ct)).Error.Should().BeNull();
        Mandato(instance).MandateFormatVersion.Should().Be(2);

        // El Super Admin publica la versión 3: lo ya emitido no cambia.
        provider.Versions[3] = "Versión tres {{placa}}";
        provider.Current = 3;
        Mandato(instance).MandateFormatVersion.Should().Be(2, "publicar no toca el adjunto emitido");

        // El flujo regenera el expediente: usa la 2 registrada, no la vigente.
        (await handler.HandleAsync(InstanceId, TenantId, Ct)).Error.Should().BeNull();

        provider.PinnedRequests.Last().Should().Be(2);
        generator.Captured!.CustomTemplateBody.Should().Be("Versión dos {{placa}}");
        Mandato(instance).MandateFormatVersion.Should().Be(2);
    }

    [Fact]
    public async Task AC4_RegeneracionDeUnContratoEmitidoSinPersonalizacion_NoAdoptaLaVersionPublicadaDespues()
    {
        var provider = new FakeProvider();
        var (handler, generator, instance) = Build(provider, Config());
        (await handler.HandleAsync(InstanceId, TenantId, Ct)).Error.Should().BeNull();

        provider.Versions[1] = "Plantilla nueva";
        provider.Current = 1;
        (await handler.HandleAsync(InstanceId, TenantId, Ct)).Error.Should().BeNull();

        generator.Captured!.CustomTemplateKind.Should().Be(MandatoCustomTemplateKindCodes.None);
        Mandato(instance).MandateFormatVersion.Should().Be(0);
    }

    [Fact]
    public async Task AC8_PlantillaPropiaHeredadaDelOt_NoSeReactivaNiSePisa_ElFormatoNoAplica()
    {
        var provider = new FakeProvider { Current = 1 };
        provider.Versions[1] = "Plantilla del formato";
        var (handler, generator, instance) = Build(provider, Config("editor", "Cuerpo heredado del OT {{placa}}"));

        var (_, error) = await handler.HandleAsync(InstanceId, TenantId, Ct);

        error.Should().BeNull();
        generator.Captured!.CustomTemplateBody.Should().Be("Cuerpo heredado del OT {{placa}}");
        provider.PinnedRequests.Should().BeEmpty("con plantilla propia heredada no se consulta el formato");
        Mandato(instance).MandateFormatVersion.Should().BeNull();
    }

    [Fact]
    public async Task SinProveedor_ElMandatoSaleComoAntes()
    {
        var instance = NewInstance();
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveByOfficeIdAsync(Ot, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(Config());
        var generator = new CapturingMandatoGenerator();
        var handler = new GenerarFurHandler(
            _repo, new MockFurDocumentGenerator(), Substitute.For<IKyverumCertificateClient>(),
            Substitute.For<IRuesCertificateGenerator>(), Substitute.For<IRnmcCertificateGenerator>(),
            Substitute.For<IProcedureInstancePrendaRepository>(), new FakeStorage(),
            NullLogger<GenerarFurHandler>.Instance, mandatoGenerator: generator, mandatePolicy: policy);

        (await handler.HandleAsync(InstanceId, TenantId, Ct)).Error.Should().BeNull();

        generator.Captured!.CustomTemplateKind.Should().Be(MandatoCustomTemplateKindCodes.None);
        Mandato(instance).MandateFormatVersion.Should().BeNull();
    }

    [Fact]
    public async Task AC6_TipoDelFormatoNoCambiaElModoDelTramite_PrevaleceLaReglaDeLaCompania()
    {
        // El formato asociado a «Mandato abierto» es solo el valor por defecto de la redacción: el modo del trámite lo
        // resuelve la regla compañía×OT (Persona natural = signer) y el generador no consulta el tipo del formato.
        var provider = new FakeProvider { Current = 1 };
        provider.Versions[1] = "Plantilla {{placa}}";
        var config = Config() with { AssignmentMode = "signer" };
        var (handler, generator, _) = Build(provider, config);

        (await handler.HandleAsync(InstanceId, TenantId, Ct)).Error.Should().BeNull();

        MandatoAssignmentModeCodes.ResolveEffective(companyRuleMode: "signer", otConfigMode: "open", otConfigExists: true)
            .Should().Be("signer");
        generator.Captured!.CustomTemplateBody.Should().Be("Plantilla {{placa}}");
    }
}

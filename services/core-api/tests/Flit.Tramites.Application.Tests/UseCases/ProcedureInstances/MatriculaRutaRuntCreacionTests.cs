using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Epic #12550 (HU #12648, AC3/AC4) — al crear el trámite desde la consulta del paso 1, la Ruta Corta
/// fija el organismo que el RUNT reporta (ignorando el que envíe el cliente) y no se crea nada si la
/// compañía no lo tiene habilitado. Mismo alcance que <see cref="CreateProcedureInstanceFromConsultaHandlerTests"/>:
/// se prueba la decisión ANTES de crear (las dependencias de la creación son clases selladas), y el
/// «pasa a la siguiente etapa» se reconoce porque el error ya no es el de la secretaría.
/// </summary>
public sealed class MatriculaRutaRuntCreacionTests
{
    private const string OtEnvigado = "STRIA TTEyTTO ENVIGADO";
    private static readonly Guid EnvigadoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeResolver _resolver = Substitute.For<ITransitOfficeResolver>();
    private readonly IPreflightPreviewStore _previewStore = Substitute.For<IPreflightPreviewStore>();

    private CreateProcedureInstanceFromConsultaHandler BuildHandler()
    {
        var preflightHandler = new RunPreflightHandler(
            _repo,
            Substitute.For<IConsultationProviderRegistry>(),
            Substitute.For<IConsultationProviderChainResolver>(),
            Substitute.For<IConsultationTenantOverrideProvider>(),
            Substitute.For<IConsultationRestrictionPolicy>(),
            _resolver);

        return new CreateProcedureInstanceFromConsultaHandler(
            _repo, new CreateProcedureInstanceHandler(_repo, _typeRepo), new PatchFieldValuesHandler(_repo),
            preflightHandler, _previewStore, _resolver, typeRepo: _typeRepo);
    }

    private static PreflightVehicleSnapshot Snapshot(params HydratedField[] fields) =>
        new([], fields, ["kyverum_runt"]);

    private static CreateFromConsultaRequest Matricula(Guid tenant, Guid? transitOfficeId) =>
        new(tenant, Guid.NewGuid(), "matricula_inicial",
            Vin: "9FBHJD209VM679113", Plate: null,
            OwnerDocumentType: null, OwnerDocumentNumber: null, PreviewToken: "tok",
            TransitOfficeId: transitOfficeId);

    [Fact]
    public async Task RutaCorta_OrganismoNoHabilitado_NoCreaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        _previewStore.TryTake(tenant, "tok").Returns(Snapshot(
            new HydratedField("plate", "WVT948", null), new HydratedField("transit_office_name", OtEnvigado, null)));
        _resolver.ResolveEnabledByNameAsync(tenant, OtEnvigado, ct).Returns((ResolvedTransitOffice?)null);

        var (result, error, _, vehicleState) = await BuildHandler().HandleAsync(Matricula(tenant, Guid.NewGuid()), ct);

        result.Should().BeNull();
        error.Should().Be(VehicleStatePolicy.OrganismoRuntNoHabilitadoErrorCode);
        vehicleState!.Detalle.Should().Be(OtEnvigado);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ProcedureInstance>(), Arg.Any<CancellationToken>());
        // El organismo que mandó el cliente ni se mira: en Ruta Corta manda el del RUNT.
        await _resolver.DidNotReceive().ResolveEnabledByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RutaCorta_SinSecretariaDelCliente_NoExigeElegirla()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        _previewStore.TryTake(tenant, "tok").Returns(Snapshot(
            new HydratedField("plate", "WVT948", null), new HydratedField("transit_office_name", OtEnvigado, null)));
        _resolver.ResolveEnabledByNameAsync(tenant, OtEnvigado, ct)
            .Returns(new ResolvedTransitOffice(EnvigadoId, "05266000", "Secretaría de Envigado", "05266", "ENVIGADO"));

        var (_, error, _, _) = await BuildHandler().HandleAsync(Matricula(tenant, null), ct);

        error.Should().NotBe(TransitOfficeSelectionPolicy.RequiredErrorCode)
            .And.NotBe(VehicleStatePolicy.OrganismoRuntNoHabilitadoErrorCode);
        await _resolver.DidNotReceive().ResolveEnabledByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RutaLarga_SinSecretaria_SigueExigiendola()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        _previewStore.TryTake(tenant, "tok").Returns(Snapshot(new HydratedField("vehicle_brand", "RENAULT", null)));

        var (_, error, _, _) = await BuildHandler().HandleAsync(Matricula(tenant, null), ct);

        error.Should().Be(TransitOfficeSelectionPolicy.RequiredErrorCode);
        await _resolver.DidNotReceive().ResolveEnabledByNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinConsultaGuardada_NoSePuedeSaberLaRuta_YSeExigeLaSecretariaComoSiempre()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        _previewStore.TryTake(tenant, "tok").Returns((PreflightVehicleSnapshot?)null);

        var (_, error, _, _) = await BuildHandler().HandleAsync(Matricula(tenant, null), ct);

        error.Should().Be(TransitOfficeSelectionPolicy.RequiredErrorCode);
    }
}

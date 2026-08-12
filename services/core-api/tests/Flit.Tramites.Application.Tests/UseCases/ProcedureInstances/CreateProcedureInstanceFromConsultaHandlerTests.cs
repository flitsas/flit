using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU sin ADO 2026-08-11 (segunda tanda) — casillas 18/19 del FUR al crear el trámite desde la
/// consulta del paso 1 (<see cref="CreateProcedureInstanceFromConsultaHandler"/>). Cubre solo la
/// VALIDACIÓN de <c>tipoServicioCode</c> (se resuelve ANTES de tocar el repositorio/crear nada), no
/// el camino feliz completo: <see cref="CreateProcedureInstanceHandler"/> y
/// <see cref="RunPreflightHandler"/> son clases selladas con dependencias propias (no hay un test
/// previo de este handler que las mockee) — habilitarlas de punta a punta es un esfuerzo aparte del
/// alcance de esta HU. Lo que SÍ prueban estos tests: (1) un código fuera del catálogo cerrado se
/// rechaza ANTES de crear el trámite, y (2) un código válido (o inválido en traspaso, donde no se
/// evalúa) NO dispara ese rechazo — deja pasar a la siguiente etapa, identificable porque el error
/// que sale ya no es <c>invalid_tipo_servicio</c>.
/// </summary>
public sealed class CreateProcedureInstanceFromConsultaHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeResolver _transitOfficeResolver = Substitute.For<ITransitOfficeResolver>();
    private readonly IPreflightPreviewStore _previewStore = Substitute.For<IPreflightPreviewStore>();

    private CreateProcedureInstanceFromConsultaHandler BuildHandler()
    {
        var createHandler = new CreateProcedureInstanceHandler(_repo, _typeRepo);
        var patchHandler = new PatchFieldValuesHandler(_repo);
        var preflightHandler = new RunPreflightHandler(
            _repo,
            Substitute.For<IConsultationProviderRegistry>(),
            Substitute.For<IConsultationProviderChainResolver>(),
            Substitute.For<IConsultationTenantOverrideProvider>(),
            Substitute.For<IConsultationRestrictionPolicy>(),
            _transitOfficeResolver);

        return new CreateProcedureInstanceFromConsultaHandler(
            _repo, createHandler, patchHandler, preflightHandler, _previewStore, _transitOfficeResolver);
    }

    private void SetUpMatriculaHappyPathHastaLaValidacion(Guid tenantId, Guid transitOfficeId)
    {
        _transitOfficeResolver
            .ResolveEnabledByIdAsync(tenantId, transitOfficeId, Arg.Any<CancellationToken>())
            .Returns(new ResolvedTransitOffice(transitOfficeId, "25286000", "OT PRUEBA", "FUNZA"));
        _repo.FindTramitesByVinAsync(tenantId, Arg.Any<string>(), Guid.Empty, Arg.Any<CancellationToken>())
            .Returns([]);
    }

    [Fact]
    public async Task TipoServicioCode_FueraDelCatalogo_EnMatricula_RechazaAntesDeCrear()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var transitOfficeId = Guid.NewGuid();
        SetUpMatriculaHappyPathHastaLaValidacion(tenantId, transitOfficeId);

        var request = new CreateFromConsultaRequest(
            tenantId, Guid.NewGuid(), "matricula_inicial",
            Vin: "1HGCM82633A004352", Plate: null,
            OwnerDocumentType: null, OwnerDocumentNumber: null, PreviewToken: null,
            TransitOfficeId: transitOfficeId,
            TipoServicioCode: "TAXI_COLECTIVO"); // no es uno de los 6 códigos cerrados

        var (result, error, existingId, vehicleState) = await BuildHandler().HandleAsync(request, ct);

        error.Should().Be("invalid_tipo_servicio");
        result.Should().BeNull();
        existingId.Should().BeNull();
        vehicleState.Should().BeNull();
        // No debió llegar a resolver el tipo de trámite (createHandler): la validación corta antes.
        await _typeRepo.DidNotReceive().GetByCodePublishedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("PARTICULAR")]
    [InlineData("publico")] // case-insensitive, igual que el resto del handler (Trim + ToUpperInvariant)
    [InlineData("OTROS")]
    public async Task TipoServicioCode_ValidoEnMatricula_NoRechazaPorCatalogo(string codigo)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var transitOfficeId = Guid.NewGuid();
        SetUpMatriculaHappyPathHastaLaValidacion(tenantId, transitOfficeId);

        var request = new CreateFromConsultaRequest(
            tenantId, Guid.NewGuid(), "matricula_inicial",
            Vin: "1HGCM82633A004352", Plate: null,
            OwnerDocumentType: null, OwnerDocumentNumber: null, PreviewToken: null,
            TransitOfficeId: transitOfficeId,
            TipoServicioCode: codigo);

        var (_, error, _, _) = await BuildHandler().HandleAsync(request, ct);

        // typeRepo no está configurado para devolver un ProcedureType real: el flujo sigue hasta
        // CreateProcedureInstanceHandler y falla ahí con "modalidad_not_available" (no hay tipo
        // publicado en el doble). Lo que importa aquí es que NO sea "invalid_tipo_servicio": la
        // validación del código dejó pasar y delegó en la siguiente etapa.
        error.Should().NotBe("invalid_tipo_servicio");
    }

    [Fact]
    public async Task TipoServicioCode_InvalidoEnTraspaso_SeIgnoraSinValidar()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        _repo.FindTramitesByPlacaAsync(tenantId, "ABC123", Guid.Empty, Arg.Any<CancellationToken>())
            .Returns([]);

        var request = new CreateFromConsultaRequest(
            tenantId, Guid.NewGuid(), "traspaso",
            Vin: null, Plate: "ABC123",
            OwnerDocumentType: "CC", OwnerDocumentNumber: "123", PreviewToken: null,
            TransitOfficeId: null,
            // Ninguno de los 6 códigos cerrados — pero en traspaso el handler NO valida ni usa este
            // campo (casilla 18/19 son de matrícula inicial exclusivamente), así que no debe rechazar.
            TipoServicioCode: "NO_ES_UN_CODIGO_VALIDO");

        var (_, error, _, _) = await BuildHandler().HandleAsync(request, ct);

        error.Should().NotBe("invalid_tipo_servicio");
        // Tampoco debió consultar la secretaría: eso es exclusivo de matrícula inicial.
        await _transitOfficeResolver.DidNotReceive()
            .ResolveEnabledByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}

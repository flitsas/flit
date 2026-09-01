using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0053 (Múltiple Propietario) — reglas de negocio del PUT de actores con hasta 4 propietarios
/// por lado (§4.1/§4.4/§4.5 del diseño técnico). Cubre: regresión cero del caso de 1 actor por lado,
/// reparto de propiedad (suma=100, ninguno en 0), duplicidad de dos niveles (intra-lado siempre;
/// entre lados solo 1-a-1), el máximo de 4 y el ordinal principal irremplazable.
///
/// <para>La cobertura de <c>IdentityApprovalResolver</c> ("todos firman") vive en
/// <see cref="MultiplePropietarioIdentityApprovalTests"/>: el resolver es <c>internal</c> y se
/// ejercita por el camino público (<see cref="GetWizardStateHandler"/>), mismo criterio que
/// <c>PredicadoActorJuridicoUnicoTests</c>.</para>
/// </summary>
public sealed class MultiplePropietarioActorsHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly BiometricsProviderOptions _providerOptions = new() { Provider = BiometricProviders.Mock };
    private readonly IKyverumVerifyClient _kyverumClient = Substitute.For<IKyverumVerifyClient>();
    private readonly IniciarKyverumVerifyHandler _kyverumHandler;
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly PutActorsHandler _put;

    private static readonly Guid BuyerEntityId = Guid.NewGuid();
    private static readonly Guid OwnerEntityId = Guid.NewGuid();

    public MultiplePropietarioActorsHandlerTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        _kyverumHandler = new IniciarKyverumVerifyHandler(
            _repo,
            _kyverumClient,
            new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(),
            Substitute.For<IIdentityValidationAuditLog>());
        _put = new PutActorsHandler(_repo, _catalogRepo, _providerOptions, _kyverumHandler, _consentRepo);

        _catalogRepo.GetProcedureEntityByCodeAsync("BUYER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = BuyerEntityId, Code = "BUYER", Name = "Comprador" });
        _catalogRepo.GetProcedureEntityByCodeAsync("OWNER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = OwnerEntityId, Code = "OWNER", Name = "Propietario" });
    }

    private static ProcedureInstance Instance(
        Guid id, Guid tenant, string modalidad = "traspaso", string? tipologia = null) =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For(tipologia ?? modalidad),
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000002",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ActorInput Comprador(
        string doc = "123", string email = "comprador@x.com", int ordinal = 1, decimal? porcentaje = null) =>
        new("comprador", "CC", doc, "Juan Comprador " + doc, email, "3001112233", Ordinal: ordinal, Porcentaje: porcentaje);

    private static ActorInput Vendedor(
        string doc = "999", string email = "vendedor@x.com", int ordinal = 1, decimal? porcentaje = null) =>
        new("vendedor", "CC", doc, "Pedro Vendedor " + doc, email, null, Ordinal: ordinal, Porcentaje: porcentaje);

    // ── Regresión cero: 1 actor por lado ────────────────────────────────────────

    [Fact]
    public async Task Put_UnActorPorLado_Ordinal1_PorcentajeNull_ComportamientoIdenticoAlActual()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([Vendedor(), Comprador()]), ct);

        error.Should().BeNull();
        result!.Actors.Should().HaveCount(2);
        result.Actors.Should().OnlyContain(a => a.Ordinal == 1);
        result.Actors.Should().OnlyContain(a => a.Porcentaje == null);
    }

    [Fact]
    public async Task Put_UnActorPorLado_PorcentajeEnviadoSeIgnora_NoBloquea()
    {
        // §4.1 del contrato: "1 actor en el rol → porcentaje debe venir null (si viene con valor, se
        // ignora)". Un cliente que mande un porcentaje "de más" con un solo actor no debe romper nada.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(
            id, tenant, new PutActorsRequest([Comprador(porcentaje: 60m)]), ct);

        error.Should().BeNull();
        result!.Actors.Single().Porcentaje.Should().BeNull();
    }

    // ── Reparto de propiedad — autoritativo en backend ──────────────────────────

    [Fact]
    public async Task Put_DosActores_SumaDistintaDe100_ReturnsPorcentajesNoSuman100()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Comprador(doc: "1", ordinal: 1, porcentaje: 60m),
            Comprador(doc: "2", ordinal: 2, porcentaje: 30m),
        ]), ct);

        error.Should().Be("porcentajes_no_suman_100");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_DosActores_SumaExacta100_EsAceptado()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Comprador(doc: "1", ordinal: 1, porcentaje: 60m),
            Comprador(doc: "2", ordinal: 2, porcentaje: 40m),
        ]), ct);

        error.Should().BeNull();
        result!.Actors.Should().HaveCount(2);
        result.Actors.Single(a => a.NumeroDocumento == "1").Porcentaje.Should().Be(60m);
        result.Actors.Single(a => a.NumeroDocumento == "2").Porcentaje.Should().Be(40m);
    }

    [Fact]
    public async Task Put_ActorConPorcentajeEnCero_ReturnsPorcentajeEnCero()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Comprador(doc: "1", ordinal: 1, porcentaje: 100m),
            Comprador(doc: "2", ordinal: 2, porcentaje: 0m),
        ]), ct);

        error.Should().Be("porcentaje_en_cero");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_ActorSinPorcentaje_ConDosActores_ReturnsPorcentajeEnCero()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Comprador(doc: "1", ordinal: 1, porcentaje: 100m),
            Comprador(doc: "2", ordinal: 2, porcentaje: null),
        ]), ct);

        error.Should().Be("porcentaje_en_cero");
        result.Should().BeNull();
    }

    // ── Duplicidad — dos niveles (§4.4) ─────────────────────────────────────────

    [Fact]
    public async Task Put_DosCompradoresMismoDocumento_ReturnsActorDuplicadoMismoLado()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Comprador(doc: "777", ordinal: 1, porcentaje: 50m),
            Comprador(doc: "777", ordinal: 2, porcentaje: 50m),
        ]), ct);

        error.Should().Be("actor_duplicado_mismo_lado");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_DosVendedoresMismoDocumento_ReturnsActorDuplicadoMismoLado()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Vendedor(doc: "888", ordinal: 1, porcentaje: 50m),
            Vendedor(doc: "888", ordinal: 2, porcentaje: 50m),
        ]), ct);

        error.Should().Be("actor_duplicado_mismo_lado");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_VendedorYCompradorMismoDocumento_1a1_SigueBloqueado_PartesDuplicadas()
    {
        // Cero regresión (§4.4 nivel 2): con exactamente 1 actor en cada lado, el bloqueo vendedor≠
        // comprador se mantiene exactamente como antes de ADR-0053.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Vendedor(doc: "555"),
            Comprador(doc: "555"),
        ]), ct);

        error.Should().Be("partes_duplicadas");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_VendedorMismoDocumentoQueComprador_ConDosCompradores_SeRelaja_EsAceptado()
    {
        // Caso de negocio confirmado (§4.4 nivel 2): A y B son copropietarios del vehículo; B le vende
        // su cuota a A. A figura como vendedor (concurre a la venta) Y como uno de los compradores
        // (aumenta su cuota) en el MISMO trámite. Con el lado comprador en 2+, la comparación cruzada
        // vendedor≠comprador se omite a propósito: sin la relajación, este PUT (con suma=100 y sin
        // duplicidad intra-lado) habría fallado con "partes_duplicadas".
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Vendedor(doc: "111"),
            Comprador(doc: "111", ordinal: 1, porcentaje: 70m),
            Comprador(doc: "222", ordinal: 2, porcentaje: 30m),
        ]), ct);

        error.Should().BeNull();
        result!.Actors.Should().Contain(a => a.Rol == "vendedor" && a.NumeroDocumento == "111");
        result.Actors.Should().Contain(a => a.Rol == "comprador" && a.NumeroDocumento == "111");
    }

    // ── Cardinalidad: máximo 4, ordinal 1..4, principal irremplazable ───────────

    [Fact]
    public async Task Put_QuintoOrdinal_ReturnsOrdinalFueraDeRango()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Comprador(doc: "1", ordinal: 1, porcentaje: 20m),
            Comprador(doc: "2", ordinal: 2, porcentaje: 20m),
            Comprador(doc: "3", ordinal: 3, porcentaje: 20m),
            Comprador(doc: "4", ordinal: 4, porcentaje: 20m),
            Comprador(doc: "5", ordinal: 5, porcentaje: 20m),
        ]), ct);

        error.Should().Be("ordinal_fuera_de_rango");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_CuatroActores_SumaOk_EsAceptado()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Comprador(doc: "1", ordinal: 1, porcentaje: 25m),
            Comprador(doc: "2", ordinal: 2, porcentaje: 25m),
            Comprador(doc: "3", ordinal: 3, porcentaje: 25m),
            Comprador(doc: "4", ordinal: 4, porcentaje: 25m),
        ]), ct);

        error.Should().BeNull();
        result!.Actors.Should().HaveCount(4);
    }

    [Fact]
    public async Task Put_OrdinalCero_ReturnsOrdinalFueraDeRango()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(
            id, tenant, new PutActorsRequest([Comprador(ordinal: 0)]), ct);

        error.Should().Be("ordinal_fuera_de_rango");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_RolPresenteSinOrdinal1_ReturnsOrdinalPrincipalAusente()
    {
        // El upsert reemplaza el rol COMPLETO: omitir ordinal=1 lo eliminaría, y el principal no se
        // puede eliminar (contrato OpenAPI, §7).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Comprador(doc: "1", ordinal: 2, porcentaje: 50m),
            Comprador(doc: "2", ordinal: 3, porcentaje: 50m),
        ]), ct);

        error.Should().Be("ordinal_principal_ausente");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_DosActoresMismoOrdinal_ReturnsOrdinalFueraDeRango()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Comprador(doc: "1", ordinal: 1, porcentaje: 50m),
            Comprador(doc: "2", ordinal: 1, porcentaje: 50m),
        ]), ct);

        error.Should().Be("ordinal_fuera_de_rango");
        result.Should().BeNull();
    }

    // ── Conjunto EFECTIVO: agregar un 2do actor en un PUT distinto al del principal ─

    [Fact]
    public async Task Put_AgregaSegundoActor_SobreElPrincipalYaPersistido_ExigeSuma100()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "1",
            FullName = "Principal",
            Email = "p@x.com",
            ProcedureEntityId = BuyerEntityId,
            Ordinal = 1,
            OwnershipPercentage = null,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        // El PUT reemplaza TODO el rol "comprador": para agregar un 2do actor hay que reenviar también
        // el ordinal=1 (el upsert es por rol completo, no por ordinal individual).
        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([
            Comprador(doc: "1", ordinal: 1, porcentaje: 60m),
            Comprador(doc: "2", ordinal: 2, porcentaje: 40m),
        ]), ct);

        error.Should().BeNull();
        result!.Actors.Should().HaveCount(2);
    }
}

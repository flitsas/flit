using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0050 — la familia OTROS no acumula trámites sobre el tipo base (art. 5.1.8 aplica a
/// matrícula y traspaso). Ahí el cambio o el gravamen ES el trámite: sumarle otro no es acumular,
/// es meter dos trámites distintos en un FUR que el organismo devuelve.
///
/// <para>Estas pruebas fijan las DOS guardas del servidor —el PATCH de <c>field_values</c> y el PUT
/// de prenda— y, en el mismo archivo, la regresión de que matrícula y traspaso conservan lo suyo.
/// Van juntas a propósito: la regla y su excepción se leen de un vistazo, y apagar los complementos
/// de más se ve como test rojo en la misma clase.</para>
/// </summary>
public sealed class OtrosSinComplementariosTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static ProcedureInstance Instance(ProcedureType type, params (string Key, string Value)[] fieldValues)
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureType = type,
            ProcedureTypeId = type.Id,
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var (key, value) in fieldValues)
        {
            instance.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                ProcedureInstanceId = instance.Id,
                FieldKey = key,
                ValueText = value,
                Source = "consultation",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        return instance;
    }

    // ── PATCH de field_values ────────────────────────────────────────────────────────────────────

    private static (PatchFieldValuesHandler Sut, IProcedureInstanceRepository Repo) PatchSut(
        ProcedureInstance instance, CancellationToken ct)
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithDetailsAsync(instance.Id, Tenant, ct).Returns(instance);
        repo.GetFormFieldIdByKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), ct).Returns((Guid?)null);
        return (new PatchFieldValuesHandler(repo), repo);
    }

    private static Task<(ProcedureInstanceDetailDto? Result, string? Error)> Patch(
        ProcedureInstance instance, CancellationToken ct, params (string Key, string? Value)[] items)
    {
        var (sut, _) = PatchSut(instance, ct);
        return sut.HandleAsync(
            instance.Id,
            Tenant,
            new PatchFieldValuesRequest([.. items.Select(i => new FieldValueInput(null, i.Key, i.Value, null))]),
            ct);
    }

    [Theory]
    [InlineData("cambio_color")]
    [InlineData("cambio_carroceria")]
    [InlineData("cambio_combustible")]
    public async Task Otros_RechazaLaBanderaDeUnaTransformacionAjenaAlTipo(string bandera)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.Blindaje);

        var (_, error) = await Patch(instance, ct, (bandera, "true"));

        error.Should().Be(PatchFieldValuesHandler.ComplementoNoAdmitidoError);
    }

    [Fact]
    public async Task Otros_RechazaTambienElValorNuevo_NoSoloLaBandera()
    {
        // El FUR declara la transformación por bandera O por diff RUNT↔efectivo. Bloquear solo la
        // bandera dejaba la puerta de atrás: bastaba escribir otro color.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.Blindaje, ("vehicle_color_runt", "BLANCO"));

        var (_, error) = await Patch(instance, ct, ("vehicle_color", "NEGRO"));

        error.Should().Be(PatchFieldValuesHandler.ComplementoNoAdmitidoError);
    }

    [Fact]
    public async Task Otros_AdmiteElAtributoQueElTipoCambiaPorDefinicion()
    {
        // No borrar la captura del tipo base al quitar los simultáneos: un CAMBIO_COLOR SÍ escribe
        // el color nuevo — es exactamente lo que el FUR tiene que imprimir.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.CambioColor, ("vehicle_color_runt", "BLANCO"));

        var (result, error) = await Patch(
            instance, ct, ("cambio_color", "true"), ("vehicle_color", "NEGRO"));

        error.Should().BeNull();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Otros_PermiteApagarUnaBanderaQueQuedoDeUnBorradorAnterior()
    {
        // Deshacer no es acumular, y es la única vía de limpiar lo que se declaró antes de la regla.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.Blindaje, ("cambio_color", "true"));

        var (_, error) = await Patch(instance, ct, ("cambio_color", "false"));

        error.Should().BeNull();
    }

    [Fact]
    public async Task Otros_NoRechazaEscribirElMismoValorDelRunt()
    {
        // Reescribir el dato del registro no declara ninguna transformación.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.Blindaje, ("vehicle_color_runt", "BLANCO"));

        var (_, error) = await Patch(instance, ct, ("vehicle_color", "blanco"));

        error.Should().BeNull();
    }

    [Theory]
    [InlineData("cambio_color")]
    [InlineData("cambio_carroceria")]
    [InlineData("cambio_combustible")]
    public async Task Traspaso_ConservaLosTramitesSimultaneos(string bandera)
    {
        // REGRESIÓN: la regla de OTROS no puede tocar a las familias que sí acumulan.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.Traspaso, ("vehicle_color_runt", "BLANCO"));

        var (_, error) = await Patch(instance, ct, (bandera, "true"), ("vehicle_color", "NEGRO"));

        error.Should().BeNull();
    }

    [Fact]
    public async Task Matricula_ConservaLosTramitesSimultaneos()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.Matricula, ("vehicle_color_runt", "BLANCO"));

        var (_, error) = await Patch(instance, ct, ("cambio_color", "true"), ("vehicle_color", "NEGRO"));

        error.Should().BeNull();
    }

    // ── PUT de prenda ────────────────────────────────────────────────────────────────────────────

    private static RegistrarPrendaHandler PrendaSut(ProcedureInstance instance, CancellationToken ct)
    {
        var instances = Substitute.For<IProcedureInstanceRepository>();
        instances.GetByIdAsync(instance.Id, Tenant, ct).Returns(instance);
        var prendas = Substitute.For<IProcedureInstancePrendaRepository>();
        prendas.GetVigenteAsync(instance.Id, Tenant, ct).Returns((ProcedureInstancePrenda?)null);
        return new RegistrarPrendaHandler(instances, prendas);
    }

    [Theory]
    [InlineData(PrendaDecision.Registrar)]
    [InlineData(PrendaDecision.Levantar)]
    [InlineData(PrendaDecision.SinPrenda)]
    public async Task Otros_NoPrendario_RechazaCualquierDecisionDeGravamen(string decision)
    {
        // Un blindaje no tiene dimensión de prenda: ni para declararla ni para negarla. Si hay que
        // levantar un gravamen, el trámite es otro.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.Blindaje);

        var (result, error) = await PrendaSut(instance, ct)
            .HandleAsync(instance.Id, Tenant, new RegistrarPrendaInput(decision), ct: ct);

        error.Should().Be(RegistrarPrendaHandler.PrendaNoAdmitidaError);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Otros_Prendario_SiAdmiteSuPropiaDecision()
    {
        // En LEVANTAMIENTO_PRENDA la prenda no es un complemento: es el trámite.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.LevantamientoPrenda);

        var (result, error) = await PrendaSut(instance, ct)
            .HandleAsync(instance.Id, Tenant, new RegistrarPrendaInput(PrendaDecision.Levantar), ct: ct);

        error.Should().BeNull();
        result!.Decision.Should().Be(PrendaDecision.Levantar);
    }

    [Fact]
    public async Task Traspaso_ConservaLaPrendaComplementaria()
    {
        // REGRESIÓN: el gravamen añadido a un traspaso sigue siendo válido (art. 5.1.8).
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.Traspaso);

        var (result, error) = await PrendaSut(instance, ct)
            .HandleAsync(instance.Id, Tenant, new RegistrarPrendaInput(PrendaDecision.Registrar, "Banco XYZ", "900123456"), ct: ct);

        error.Should().BeNull();
        result!.Decision.Should().Be(PrendaDecision.Registrar);
    }

    [Fact]
    public async Task Matricula_ConservaLaPrendaDeclarativa()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.Matricula);

        var (_, error) = await PrendaSut(instance, ct)
            .HandleAsync(instance.Id, Tenant, new RegistrarPrendaInput(PrendaDecision.SinPrenda), ct: ct);

        error.Should().BeNull();
    }
}

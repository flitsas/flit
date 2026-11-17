using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Matrícula ICT con placa de un rango del OT (plate_assignment_type=4): la secretaría queda fija; y cambio de
/// carrocería simultáneo sobre un vehículo sin carrocería (modos block/warn/off).
/// </summary>
public sealed class PatchFieldValuesPlacaRangoYCarroceriaTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtId = Guid.NewGuid();

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private PatchFieldValuesHandler Sut(TramiteValidationMode? carroceria = null) =>
        new(_repo, carroceria is null
            ? null
            : new TramiteValidationPolicy(TramiteValidationMode.Block, TramiteValidationMode.Block, carroceria.Value));

    private ProcedureInstance Arrange(ProcedureType tipo, params (string Key, string? Value, string Source)[] campos)
    {
        var id = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = tipo,
            Id = id,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            TransitOfficeId = OtId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var (key, value, source) in campos)
        {
            instance.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ProcedureInstanceId = id,
                FieldKey = key, ValueText = value, Source = source,
            });
        }

        _repo.GetByIdWithDetailsAsync(id, TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetFormFieldIdByKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
        return instance;
    }

    private static (string, string?, string)[] PlacaRango(params (string, string?, string)[] extra) =>
    [
        ("plate", "QZT101", "ict"),
        ("plate_assignment_type", "4", "ict"),
        ("transit_office_id", OtId.ToString(), "user"),
        ("transit_office_code", "11001000", "user"),
        .. extra,
    ];

    // ---- OT fijo (tipo 4) ----

    [Fact]
    public async Task Tipo4_CambiarElOrganismo_SeRechazaConElCodigoEstableYLaPlaca()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, PlacaRango());

        var (result, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "transit_office_id", Guid.NewGuid().ToString(), null)]), ct);

        result.Should().BeNull();
        PatchFieldValuesHandler.TryParseOtFijado(error, out var placa).Should().BeTrue();
        placa.Should().Be("QZT101");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("transit_office_code", "05001000")]
    [InlineData("transit_office_name", "OTRA SECRETARIA")]
    public async Task Tipo4_CambiarCodigoONombreDelOrganismo_SeRechaza(string key, string value)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula,
            PlacaRango(("transit_office_name", "SDM BOGOTA", "user")));

        var (_, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, key, value, null)]), ct);

        PatchFieldValuesHandler.TryParseOtFijado(error, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Tipo4_ReenviarElMismoOrganismo_EsIdempotente()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, PlacaRango());

        var (result, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest(
            [
                new FieldValueInput(null, "transit_office_id", OtId.ToString().ToUpperInvariant(), null),
                new FieldValueInput(null, "transit_office_code", "11001000", null),
            ]), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData("plate", "ABC123")]
    [InlineData("plate", "")]
    [InlineData("plate_assignment_type", "3")]
    [InlineData("plate_assignment_type", "2")]
    public async Task Tipo4_CambiarLaPlacaOSuTipo_SeRechazaConPlacaFijadaPorRango(string key, string value)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, PlacaRango());

        var (result, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, key, value, null)]), ct);

        result.Should().BeNull();
        PatchFieldValuesHandler.TryParsePlacaFijada(error, out var placa).Should().BeTrue();
        placa.Should().Be("QZT101");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("plate", "QZT101")]
    [InlineData("plate", "  qzt101 ")]
    [InlineData("plate_assignment_type", "4")]
    [InlineData("plate_assignment_type", " 4 ")]
    public async Task Tipo4_ReenviarLaMismaPlacaOTipo_EsIdempotente(string key, string value)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, PlacaRango());

        var (_, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, key, value, null)]), ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task Tipo4_LaOrquestacionIct_ConOrigenIct_PuedeReescribirLaPlaca()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, PlacaRango());

        var (_, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "plate", "QZT102", null, "ict")]), ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task Tipo4_LaSiembraDeSistema_PuedeEscribirLaPlaca()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, PlacaRango());

        var (_, error) = await Sut().HandleSystemSeedAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "plate", "QZT102", null)]), ct);

        error.Should().BeNull();
    }

    [Theory]
    [InlineData("3")]
    [InlineData("2")]
    public async Task Tipos2y3_LaPlacaSigueEditable(string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula,
            ("plate", "QZT101", "ict"), ("plate_assignment_type", tipo, "ict"));

        var (_, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "plate", "ABC123", null)]), ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task PlacaNoIct_SigueEditable()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula,
            ("plate", "QZT101", "user"), ("plate_assignment_type", "4", "ict"));

        var (_, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "plate", "ABC123", null)]), ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task Tipo4_OtrosCamposSiSePuedenEditar()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, PlacaRango());

        var (_, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "vehicle_color", "ROJO", null)]), ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task Tipo4_LaSiembraDeSistemaNoEsEdicionDelUsuario()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, PlacaRango());

        var (_, error) = await Sut().HandleSystemSeedAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "transit_office_id", Guid.NewGuid().ToString(), null)]), ct);

        error.Should().BeNull();
    }

    [Theory]
    [InlineData("3")] // placa completa entregada: no sale de un rango del OT
    [InlineData("2")] // dígito de preferencia
    public async Task OtrosTiposDeAsignacion_NoFijanElOrganismo(string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula,
            ("plate", "QZT101", "ict"), ("plate_assignment_type", tipo, "ict"),
            ("transit_office_id", OtId.ToString(), "user"));

        var (_, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "transit_office_id", Guid.NewGuid().ToString(), null)]), ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task TramiteSinPlacaIct_ElOrganismoSigueEditable()
    {
        var ct = TestContext.Current.CancellationToken;
        // Placa tecleada por el gestor (source user) aunque el tipo diga 4: no es placa de rango entregada por ICT.
        var instance = Arrange(ProcedureTypeFixture.Matricula,
            ("plate", "QZT101", "user"), ("plate_assignment_type", "4", "ict"),
            ("transit_office_id", OtId.ToString(), "user"));

        var (_, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "transit_office_id", Guid.NewGuid().ToString(), null)]), ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task SinPlaca_ElOrganismoSigueEditable()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, ("transit_office_id", OtId.ToString(), "user"));

        var (_, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "transit_office_id", Guid.NewGuid().ToString(), null)]), ct);

        error.Should().BeNull();
    }

    // ---- cambio de carrocería simultáneo sin carrocería ----

    private static readonly (string, string?, string)[] VehiculoSinCarroceria =
    [
        ("vehicle_class", "MOTOCICLETA", "consultation"),
        ("vehicle_body_type_runt", "SIN CARROCERIA", "consultation"),
    ];

    [Fact]
    public async Task CambioCarroceriaSimultaneo_SinCarroceria_Block_Rechaza422()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, VehiculoSinCarroceria);

        var (result, error) = await Sut(TramiteValidationMode.Block).HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "cambio_carroceria", "true", null)]), ct);

        result.Should().BeNull();
        error.Should().Be("VEHICLE_BODY_TYPE_MISSING");
    }

    [Fact]
    public async Task CambioCarroceriaSimultaneo_SinCarroceria_PorDefectoEsBlock()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, VehiculoSinCarroceria);

        var (_, error) = await Sut().HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "cambio_carroceria", "true", null)]), ct);

        error.Should().Be("VEHICLE_BODY_TYPE_MISSING");
    }

    [Theory]
    [InlineData(TramiteValidationMode.Warn)]
    [InlineData(TramiteValidationMode.Off)]
    public async Task CambioCarroceriaSimultaneo_SinCarroceria_WarnYOff_NoBloquean(TramiteValidationMode modo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, VehiculoSinCarroceria);

        var (_, error) = await Sut(modo).HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "cambio_carroceria", "true", null)]), ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task CambioCarroceriaSimultaneo_ConCarroceria_Pasa()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula,
            ("vehicle_class", "AUTOMOVIL", "consultation"), ("vehicle_body_type_runt", "SEDAN", "consultation"));

        var (_, error) = await Sut(TramiteValidationMode.Block).HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "cambio_carroceria", "true", null)]), ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task CambioCarroceriaSimultaneo_SinConsultaRunt_NoBloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        // Ausencia de respuesta no es ausencia de carrocería.
        var instance = Arrange(ProcedureTypeFixture.Matricula);

        var (_, error) = await Sut(TramiteValidationMode.Block).HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "cambio_carroceria", "true", null)]), ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task ApagarCambioCarroceria_SinCarroceria_SiempreSePermite()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Arrange(ProcedureTypeFixture.Matricula, VehiculoSinCarroceria);

        var (_, error) = await Sut(TramiteValidationMode.Block).HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "cambio_carroceria", "false", null)]), ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task TramitePrincipalCambioCarroceria_NoCambiaDeComportamiento()
    {
        var ct = TestContext.Current.CancellationToken;
        // Lo cubren el preflight y el gate de preparación; este PATCH no lo rechaza.
        var instance = Arrange(ProcedureTypeFixture.CambioCarroceria, VehiculoSinCarroceria);

        var (_, error) = await Sut(TramiteValidationMode.Block).HandleAsync(instance.Id, TenantId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "cambio_carroceria", "true", null)]), ct);

        error.Should().BeNull();
    }
}

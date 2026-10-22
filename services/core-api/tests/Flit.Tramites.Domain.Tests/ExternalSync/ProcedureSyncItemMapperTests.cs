using System.Text.Json;
using Flit.Tramites.Domain.ExternalSync;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.ExternalSync;

/// <summary>
/// HU #13079 — armado del ítem del feed desde la fila cruda (contrato v3.1 §4).
/// <code>var item = ProcedureSyncItemMapper.Map(fila, nombresDeTiposDeServicio);</code>
/// </summary>
public sealed class ProcedureSyncItemMapperTests
{
    private static readonly Dictionary<string, string> Servicios = new(StringComparer.Ordinal)
    {
        ["PARTICULAR"] = "Particular",
        ["PUBLICO"] = "Público",
    };

    private static readonly JsonSerializerOptions ComoElContrato = new(JsonSerializerDefaults.Web);

    /// <summary>Claves del ejemplo del contrato v3.1 §4, por bloque.</summary>
    private static readonly string[] ClavesItem =
    [
        "id", "radicado", "consecutivo", "syncVersion", "fechaUltimoCambio", "eliminado", "estado", "tramite",
        "fechaCreacion", "fechaRadicacion", "fechaAprobacion", "vehiculo", "organismo", "compradores", "factura",
        "companiaGestora",
    ];

    private static readonly string[] ClavesVehiculo =
    [
        "vin", "placa", "clase", "marca", "linea", "modeloAno", "carroceria", "cilindraje", "cilindrajeTexto",
        "capacidad", "numeroMotor", "numeroSerie", "tipoServicio",
    ];

    private static readonly string[] ClavesOrganismo = ["codigoTransito", "nombre", "codigoSecretaria", "ciudad", "departamento"];

    private static readonly string[] ClavesComprador =
    [
        "ordinal", "porcentajeParticipacion", "rolActor", "tipoPersona", "tipoDocumento", "numeroDocumento",
        "nombreCompleto", "direccion", "ciudad", "celular", "correo",
    ];

    [Fact]
    public void AC1_ElItemTraeTodasLasClavesDelContratoConNulosExplicitos()
    {
        var item = ProcedureSyncItemMapper.Map(Fila(), Servicios);

        var json = JsonSerializer.SerializeToElement(item, ComoElContrato);

        Claves(json).Should().Equal(ClavesItem);
        Claves(json.GetProperty("tramite")).Should().Equal("codigo", "nombre", "familia");
        Claves(json.GetProperty("vehiculo")).Should().Equal(ClavesVehiculo);
        Claves(json.GetProperty("vehiculo").GetProperty("tipoServicio")).Should().Equal("codigo", "nombre");
        Claves(json.GetProperty("organismo")).Should().Equal(ClavesOrganismo);
        Claves(json.GetProperty("compradores")[0]).Should().Equal(ClavesComprador);
        Claves(json.GetProperty("factura")).Should().Equal("adjuntoId", "nombreArchivo", "cargadaEn");
        Claves(json.GetProperty("companiaGestora")).Should().Equal("tenantId", "nit", "nombre");

        json.GetProperty("fechaAprobacion").ValueKind.Should().Be(JsonValueKind.Null, "sin aprobación la clave va en null");
        json.GetProperty("vehiculo").GetProperty("cilindrajeTexto").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("compradores")[0].GetProperty("porcentajeParticipacion").ValueKind.Should().Be(JsonValueKind.Null,
            "con un solo comprador el porcentaje va en null (equivale a 100)");
    }

    [Fact]
    public void AC1_ValoresMapeadosYFechasConOffsetDeColombia()
    {
        var item = ProcedureSyncItemMapper.Map(Fila(), Servicios);

        item.Radicado.Should().Be("FT1-0001234");
        item.Estado.Should().Be("entregado");
        item.Tramite.Should().Be(new ProcedureSyncTramite("MATRICULA_NUEVA", "Matrícula inicial", "MATRICULAS"));
        item.Vehiculo!.ModeloAno.Should().Be(2026);
        item.Vehiculo.Cilindraje.Should().Be(2000);
        item.Vehiculo.Capacidad.Should().Be(5);
        item.Vehiculo.TipoServicio.Should().Be(new ProcedureSyncTipoServicio("PARTICULAR", "Particular"));
        item.Organismo!.CodigoTransito.Should().Be("76520000");
        item.Factura!.NombreArchivo.Should().Be("factura.pdf");
        item.CompaniaGestora.Nit.Should().Be("901000000");
        item.FechaCreacion.Offset.Should().Be(TimeSpan.FromHours(-5));
        item.FechaUltimoCambio.Offset.Should().Be(TimeSpan.FromHours(-5));
        item.Factura.CargadaEn.Offset.Should().Be(TimeSpan.FromHours(-5));
    }

    [Fact]
    public void AC2_CopropiedadOrdenadaPorOrdinalConSuPorcentaje()
    {
        var fila = Fila() with
        {
            Actors = [Actor(2, 40.00m, "1000000000"), Actor(1, 60.00m, "900000000")],
        };

        var compradores = ProcedureSyncItemMapper.Map(fila, Servicios).Compradores;

        compradores.Select(c => c.Ordinal).Should().Equal(1, 2);
        compradores.Select(c => c.PorcentajeParticipacion).Should().Equal(60.00m, 40.00m);
        compradores.Select(c => c.NumeroDocumento).Should().Equal("900000000", "1000000000");
    }

    [Fact]
    public void AC3_LosPropietariosLleganConSuRolYSinActoresLaListaEsVacia()
    {
        var conPropietario = ProcedureSyncItemMapper.Map(Fila() with { Actors = [Actor(1, null, "1000000000", "propietario")] }, Servicios);
        var sinActores = ProcedureSyncItemMapper.Map(Fila() with { Actors = [] }, Servicios);

        conPropietario.Compradores.Should().ContainSingle().Which.RolActor.Should().Be("propietario");
        sinActores.Compradores.Should().NotBeNull().And.BeEmpty();
        sinActores.Eliminado.Should().BeFalse("el ítem se entrega igual");
    }

    [Fact]
    public void AC4_LaMarcaDeBorradoVaciaLosBloquesYConservaLaIdentidad()
    {
        var item = ProcedureSyncItemMapper.Map(Fila() with { IsDeleted = true }, Servicios);

        item.Eliminado.Should().BeTrue();
        item.Vehiculo.Should().BeNull();
        item.Organismo.Should().BeNull();
        item.Factura.Should().BeNull();
        item.Compradores.Should().NotBeNull().And.BeEmpty();
        item.Radicado.Should().Be("FT1-0001234");
        item.Estado.Should().Be("entregado");
        item.CompaniaGestora.TenantId.Should().NotBeEmpty();
    }

    [Fact]
    public void AC5_CilindrajeNoNumericoVaEnNullYElTextoCrudoEnCilindrajeTexto()
    {
        var vehiculo = ProcedureSyncItemMapper.Map(Fila() with { VehicleEngineDisplacement = "2.0 L" }, Servicios).Vehiculo!;

        vehiculo.Cilindraje.Should().BeNull();
        vehiculo.CilindrajeTexto.Should().Be("2.0 L");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DecisionA_SinTipoDeServicioVaEnNullSinAsumirParticular(string? crudo)
    {
        ProcedureSyncItemMapper.Map(Fila() with { VehicleService = crudo }, Servicios).Vehiculo!.TipoServicio.Should().BeNull();
    }

    [Fact]
    public void DecisionA_ElTextoDelRuntSeNormalizaAlCodigoDelCatalogo()
    {
        ProcedureSyncItemMapper.Map(Fila() with { VehicleService = "servicio publico" }, Servicios).Vehiculo!.TipoServicio
            .Should().Be(new ProcedureSyncTipoServicio("PUBLICO", "Público"));
    }

    [Fact]
    public void DecisionB_ModeloYCapacidadNoNumericosVanEnNullSinTexto()
    {
        var vehiculo = ProcedureSyncItemMapper.Map(Fila() with { VehicleYear = "dos mil", VehiclePassengers = "5 pasajeros" }, Servicios).Vehiculo!;

        vehiculo.ModeloAno.Should().BeNull();
        vehiculo.Capacidad.Should().BeNull();
    }

    [Fact]
    public void LosTextosVaciosLleganEnNullYNuncaVacios()
    {
        var item = ProcedureSyncItemMapper.Map(
            Fila() with { VehicleBrand = "  ", Plate = "", TenantTaxId = " ", Actors = [Actor(1, null, " ") with { Email = "" }] },
            Servicios);

        item.Vehiculo!.Marca.Should().BeNull();
        item.Vehiculo.Placa.Should().BeNull();
        item.CompaniaGestora.Nit.Should().BeNull();
        item.Compradores[0].NumeroDocumento.Should().BeNull();
        item.Compradores[0].Correo.Should().BeNull();
    }

    [Fact]
    public void SinOrganismoNiFacturaLosBloquesVanEnNull()
    {
        var item = ProcedureSyncItemMapper.Map(
            Fila() with
            {
                TransitOfficeCode = null, TransitOfficeName = null, TransitOfficeCityCode = null,
                TransitOfficeCityName = null, TransitOfficeDepartmentName = null,
                InvoiceAttachmentId = null, InvoiceFilename = null, InvoiceUploadedAt = null,
            },
            Servicios);

        item.Organismo.Should().BeNull();
        item.Factura.Should().BeNull();
    }

    private static string[] Claves(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToArray();

    private static ProcedureSyncActorRow Actor(int ordinal, decimal? porcentaje, string documento, string rol = "comprador") =>
        new(ordinal, porcentaje, rol, "natural", "CC", documento, "PERSONA EJEMPLO", "CALLE 1 # 2-3", "PALMIRA", "3000000000",
            "persona@ejemplo.test");

    private static ProcedureSyncRow Fila() => new(
        Id: Guid.Parse("0192b7c4-5e6a-7d10-9f21-3a4b5c6d7e8f"),
        Position: new ProcedureSyncPosition(900, 48213),
        ReferenceNumber: "FT1-0001234",
        Consecutivo: 1234,
        ChangedAt: new DateTimeOffset(2026, 9, 21, 15, 14, 55, TimeSpan.Zero),
        IsDeleted: false,
        Status: "entregado",
        ProcedureTypeCode: "MATRICULA_NUEVA",
        ProcedureTypeName: "Matrícula inicial",
        ProcedureTypeFamily: "MATRICULAS",
        CreatedAt: new DateTimeOffset(2026, 9, 1, 13, 0, 0, TimeSpan.Zero),
        SubmittedAt: new DateTimeOffset(2026, 9, 1, 14, 30, 0, TimeSpan.Zero),
        ApprovedAt: null,
        Vin: "1HGBH41JXMN109186",
        Plate: "ABC123",
        VehicleClass: "CAMIONETA",
        VehicleBrand: "MARCA EJEMPLO",
        VehicleLine: "LINEA EJEMPLO",
        VehicleYear: "2026",
        VehicleBodyType: "SUV",
        VehicleEngineDisplacement: "2000",
        VehiclePassengers: "5",
        VehicleEngineNumber: "MTR000000",
        VehicleSeries: "SER000000",
        VehicleService: "PARTICULAR",
        TransitOfficeCode: "76520000",
        TransitOfficeName: "SECRETARIA DE TRANSITO EJEMPLO",
        TransitOfficeCityCode: "76520",
        TransitOfficeCityName: "PALMIRA",
        TransitOfficeDepartmentName: "VALLE DEL CAUCA",
        Actors: [Actor(1, null, "900000000")],
        InvoiceAttachmentId: Guid.Parse("0192b7c4-9a1b-7c2d-8e3f-4a5b6c7d8e9f"),
        InvoiceFilename: "factura.pdf",
        InvoiceUploadedAt: new DateTimeOffset(2026, 9, 2, 15, 0, 0, TimeSpan.Zero),
        TenantId: Guid.Parse("0189a0b1-c2d3-7e4f-a5b6-c7d8e9f0a1b2"),
        TenantTaxId: "901000000",
        TenantLegalName: "TRAMITADORA EJEMPLO SAS");
}

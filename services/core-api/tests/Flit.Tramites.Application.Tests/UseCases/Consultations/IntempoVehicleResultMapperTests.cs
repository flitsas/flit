using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

public sealed class IntempoVehicleResultMapperTests
{
    // Construye respuesta con la forma del doc §4.1 (VIN) / §4.2 (PLACA).
    private static IntempoVehicleResponse Response(
        string codigoResultado = "Exitoso",
        string estadoDelVehiculo = "ACTIVO",
        string soatEstado = "VIGENTE",
        string tieneGravamenes = "NO",
        string prendas = "NO",
        List<IntempoGravamen>? gravamenes = null,
        string? noPlaca = "QMW383",
        string? noVin = "LRW3E7FS7TC753038",
        string? modelo = "2026",
        string? marca = "TESLA",
        string? linea = "MODEL 3",
        string? color = "PLATA",
        string? claseVehiculo = "AUTOMOVIL",
        string? tipoCombustible = "ELECTRICO",
        string? cilindraje = "1991",
        string? organismoTransito = "STT BOGOTA") =>
        new()
        {
            CodigoResultado = codigoResultado,
            EstadoDelVehiculo = estadoDelVehiculo,
            NoPlaca = noPlaca,
            NoVin = noVin,
            Modelo = modelo,
            Marca = marca,
            Linea = linea,
            Color = color,
            ClaseVehiculo = claseVehiculo,
            TipoCombustible = tipoCombustible,
            Cilindraje = cilindraje,
            OrganismoTransito = organismoTransito,
            TieneGravamenes = tieneGravamenes,
            Prendas = prendas,
            SoatNacionales = [new IntempoSoat { Estado = soatEstado }],
            Gravamenes = gravamenes ?? [],
            LimitacionesPropiedad = [],
        };

    private static ConsultationCheck Check(IntempoVehicleResponse r, string key)
    {
        var result = IntempoVehicleResultMapper.Map(r);
        return result.Checks.Single(c => c.Key == key);
    }

    [Fact]
    public void VehiculoLimpio_ProduceGreen()
    {
        var result = IntempoVehicleResultMapper.Map(Response());

        result.Overall.Should().Be("green");
    }

    [Theory]
    [InlineData("ACTIVO", "ok")]
    [InlineData("INACTIVO", "fail")]
    [InlineData("DESINTEGRADO", "fail")]
    public void EstadoVehiculo_MapaCorrectamente(string estado, string expected)
    {
        Check(Response(estadoDelVehiculo: estado), "estado_vehiculo").Status.Should().Be(expected);
    }

    [Theory]
    [InlineData("VIGENTE", "ok")]
    [InlineData("VENCIDO", "fail")]
    public void Soat_VigenteOFail(string soatEstado, string expected)
    {
        Check(Response(soatEstado: soatEstado), "soat").Status.Should().Be(expected);
    }

    [Fact]
    public void Soat_ListaVacia_EsFail()
    {
        var r = Response();
        r.SoatNacionales = [];
        var result = IntempoVehicleResultMapper.Map(r);

        result.Checks.Single(c => c.Key == "soat").Status.Should().Be("fail");
    }

    [Fact]
    public void Gravamenes_SinNada_EsOk()
    {
        var result = IntempoVehicleResultMapper.Map(
            Response(tieneGravamenes: "NO", prendas: "NO"));

        result.Checks.Single(c => c.Key == "gravamenes").Status.Should().Be("ok");
    }

    [Theory]
    [InlineData("SI", "NO")]
    [InlineData("NO", "SI")]
    public void Gravamenes_ConGravamenOPrenda_EsWarn(string tieneGrav, string prendas)
    {
        var result = IntempoVehicleResultMapper.Map(
            Response(tieneGravamenes: tieneGrav, prendas: prendas));

        result.Checks.Single(c => c.Key == "gravamenes").Status.Should().Be("warn");
    }

    [Fact]
    public void CodigoResultadoError_ProduceFailRed()
    {
        var result = IntempoVehicleResultMapper.Map(Response(codigoResultado: "Error"));

        result.Overall.Should().Be("red");
        result.Checks.Should().HaveCount(1);
        result.Checks[0].Status.Should().Be("fail");
    }

    [Fact]
    public void HydratedFields_MapPlacaVinModeloMarcaLinea()
    {
        var result = IntempoVehicleResultMapper.Map(
            Response(noPlaca: "QMW383", noVin: "LRW3E7FS7TC753038", modelo: "2026",
                     marca: "TESLA", linea: "MODEL 3"));

        result.HydratedFields.Should().Contain(f => f.FieldKey == "plate" && f.ValueText == "QMW383");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vin" && f.ValueText == "LRW3E7FS7TC753038");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_year" && f.ValueText == "2026");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_brand" && f.ValueText == "TESLA");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_line" && f.ValueText == "MODEL 3");
    }

    [Fact]
    public void HydratedFields_MapFullVehicleAttributes()
    {
        var result = IntempoVehicleResultMapper.Map(
            Response(color: "PLATA", claseVehiculo: "AUTOMOVIL", tipoCombustible: "ELECTRICO",
                     cilindraje: "1991", organismoTransito: "STT BOGOTA", estadoDelVehiculo: "ACTIVO"));

        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_color" && f.ValueText == "PLATA");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_class" && f.ValueText == "AUTOMOVIL");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_fuel" && f.ValueText == "ELECTRICO");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_engine_displacement" && f.ValueText == "1991");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "transit_office_name" && f.ValueText == "STT BOGOTA");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_state" && f.ValueText == "ACTIVO");
    }

    [Fact]
    public void Provider_EsIntempo()
    {
        var result = IntempoVehicleResultMapper.Map(Response());

        result.Provider.Should().Be("intempo");
    }

    [Fact]
    public void NullResponse_DoesNotThrow()
    {
        // Respuesta con todos los campos nulos (payload inesperado).
        var result = IntempoVehicleResultMapper.Map(new IntempoVehicleResponse());

        result.Provider.Should().Be("intempo");
        result.Checks.Should().HaveCount(3);
    }

    [Fact]
    public void Overall_AnyFail_IsRed()
    {
        var result = IntempoVehicleResultMapper.Map(Response(soatEstado: "VENCIDO"));

        result.Overall.Should().Be("red");
    }

    [Fact]
    public void Overall_SoloWarn_IsYellow()
    {
        var result = IntempoVehicleResultMapper.Map(Response(prendas: "SI"));

        result.Overall.Should().Be("yellow");
    }

    // ── HU #11137 — SOAT y fecha de matrícula ────────────────────────────────

    private static IntempoVehicleResponse ConSoatCompleto()
    {
        var r = Response();
        r.FechaMatricula = "15/03/2015";
        r.TipoServicio = "PARTICULAR";
        r.TipoCarroceria = "SEDAN";
        r.NoChasis = "CH-9988";
        r.SoatNacionales =
        [
            new IntempoSoat
            {
                Estado = "VIGENTE",
                NoPoliza = "SOAT-778899",
                FechaExpedicion = "04/05/2026",
                FechaVigencia = "06/05/2026",
                FechaVencimiento = "05/05/2027",
                EntidadExpideSoat = "SEGUROS DEL ESTADO S.A.",
            },
        ];
        return r;
    }

    private static string? Valor(ConsultationResult r, string key) =>
        r.HydratedFields.FirstOrDefault(f => f.FieldKey == key)?.ValueText;

    [Fact]
    public void Soat_SeAlmacenaCompleto()
    {
        // Antes este mapper producía una verificación de estado y NINGÚN campo, así que un trámite
        // consultado por Intempo emitía la tabla certificadora del SOAT entera en blanco.
        var result = IntempoVehicleResultMapper.Map(ConSoatCompleto());

        Valor(result, "soat_poliza").Should().Be("SOAT-778899");
        Valor(result, "soat_expedicion").Should().Be("04/05/2026");
        Valor(result, "soat_vigencia").Should().Be("06/05/2026");
        Valor(result, "soat_vencimiento").Should().Be("05/05/2027");
        Valor(result, "soat_aseguradora").Should().Be("SEGUROS DEL ESTADO S.A.");
    }

    [Fact]
    public void SoatEstado_SeNormalizaAlVocabularioDelGateDelOt()
    {
        // El crudo del RUNT ("VIGENTE") bloquearía la aprobación del OT: el frontend compara estricto
        // contra "vigente" en minúscula.
        var result = IntempoVehicleResultMapper.Map(ConSoatCompleto());

        Valor(result, "soat_estado").Should().Be("vigente");
    }

    [Fact]
    public void Soat_PrefiereElVigenteSobreElVencido()
    {
        var r = ConSoatCompleto();
        r.SoatNacionales =
        [
            new IntempoSoat { Estado = "VENCIDO", NoPoliza = "VIEJA" },
            new IntempoSoat { Estado = "VIGENTE", NoPoliza = "ACTUAL" },
        ];

        Valor(IntempoVehicleResultMapper.Map(r), "soat_poliza").Should().Be("ACTUAL");
    }

    [Fact]
    public void FechaDeMatricula_SeAlmacena()
    {
        // Insumo de la regla de antigüedad de la RTM (HU #11136).
        Valor(IntempoVehicleResultMapper.Map(ConSoatCompleto()), "vehicle_registration_date")
            .Should().Be("15/03/2015");
    }

    [Fact]
    public void Intempo_AlmacenaAlMenosLasMismasLlavesDeSoatQueVerifik()
    {
        // Paridad entre los dos proveedores cuyo contrato SÍ trae el registro completo de SOAT. Si uno
        // se queda atrás, esta prueba lo dice. (Kyverum no entra: su contrato solo trae tres campos,
        // documentado en KyverumRuntSoat.)
        var intempo = IntempoVehicleResultMapper.Map(ConSoatCompleto())
            .HydratedFields.Select(f => f.FieldKey)
            .Where(k => k.StartsWith("soat_", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        intempo.Should().BeEquivalentTo(
            ["soat_poliza", "soat_vigencia", "soat_expedicion", "soat_vencimiento", "soat_aseguradora", "soat_estado"]);
    }

    [Fact]
    public void Soat_SinDatos_NoEscribeLlaves()
    {
        var r = Response();
        r.SoatNacionales = [];

        var result = IntempoVehicleResultMapper.Map(r);

        result.HydratedFields.Should().NotContain(f => f.FieldKey.StartsWith("soat_", StringComparison.Ordinal));
    }
}

using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

public sealed class VerifikResultMapperTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    // Datos ficticios con la MISMA forma que la respuesta REAL de RUNT (no PII).
    private static VerifikVehicleResponse Response(
        string? estado = "ACTIVO",
        string? soatEstado = "VIGENTE",
        string? tecnoVigente = "SI",
        string? tieneGravamenes = "NO",
        string? prendas = "NO",
        string? noPlaca = "ABC123",
        string? noVin = "1HGCM82633A004352",
        string? modelo = "2020",
        string? cilindraje = null,
        string? cilidraje = null) =>
        new()
        {
            Data = new VerifikVehicleData
            {
                InformacionGeneral = new VerifikInformacionGeneral
                {
                    EstadoDelVehiculo = estado,
                    NoPlaca = noPlaca,
                    NoVin = noVin,
                    Modelo = modelo,
                    Cilindraje = cilindraje,
                    Cilidraje = cilidraje,
                    TieneGravamenes = tieneGravamenes,
                    Prendas = prendas,
                },
                Soat = soatEstado is null ? [] : [new VerifikSoat { Estado = soatEstado }],
                TecnoMecanica = tecnoVigente is null ? [] : [new VerifikTecnomecanica { Vigente = tecnoVigente }],
                GarantiasMobiliarias = [],
            },
        };

    private static ConsultationCheck Check(ConsultationResult r, string key) =>
        r.Checks.Single(c => c.Key == key);

    [Theory]
    [InlineData("ACTIVO", "ok")]
    [InlineData("INACTIVO", "fail")]
    [InlineData("INMOVILIZADO", "fail")]
    public void EstadoVehiculo_MapsActivoToOk_OthersToFail(string estado, string expected)
    {
        var result = VerifikResultMapper.MapVehicle(Response(estado: estado));
        Check(result, "estado_vehiculo").Status.Should().Be(expected);
    }

    [Theory]
    [InlineData("VIGENTE", "ok")]
    [InlineData("VENCIDO", "fail")]
    public void Soat_VigenteIsOk_VencidoIsFail(string soatEstado, string expected)
    {
        var result = VerifikResultMapper.MapVehicle(Response(soatEstado: soatEstado));
        Check(result, "soat").Status.Should().Be(expected);
    }

    [Fact]
    public void Soat_EmptyList_IsUnknown()
    {
        var result = VerifikResultMapper.MapVehicle(Response(soatEstado: null));
        Check(result, "soat").Status.Should().Be("unknown");
    }

    [Theory]
    [InlineData("SI", "ok")]
    [InlineData("NO", "fail")]
    [InlineData("NO APLICA", "unknown")]
    public void Tecnomecanica_MapsByVigente(string vigente, string expected)
    {
        var result = VerifikResultMapper.MapVehicle(Response(tecnoVigente: vigente));
        Check(result, "tecnomecanica").Status.Should().Be(expected);
    }

    [Fact]
    public void Tecnomecanica_EmptyArray_IsUnknown()
    {
        var result = VerifikResultMapper.MapVehicle(Response(tecnoVigente: null));
        Check(result, "tecnomecanica").Status.Should().Be("unknown");
    }

    [Fact]
    public void Tecnomecanica_MultipleItems_VigenteSiWins()
    {
        var response = new VerifikVehicleResponse
        {
            Data = new VerifikVehicleData
            {
                InformacionGeneral = new VerifikInformacionGeneral { EstadoDelVehiculo = "ACTIVO" },
                TecnoMecanica =
                [
                    new VerifikTecnomecanica { Vigente = "NO" },
                    new VerifikTecnomecanica { Vigente = "SI" },
                ],
            },
        };

        var result = VerifikResultMapper.MapVehicle(response);
        Check(result, "tecnomecanica").Status.Should().Be("ok");
    }

    [Fact]
    public void Gravamenes_NoneIsOk()
    {
        var result = VerifikResultMapper.MapVehicle(
            Response(tieneGravamenes: "NO", prendas: "NO"));
        Check(result, "gravamenes").Status.Should().Be("ok");
    }

    [Theory]
    [InlineData("SI", "NO")]
    [InlineData("NO", "SI")]
    [InlineData("SI", "SI")]
    public void Gravamenes_AnyPresentIsWarn(string tiene, string prendas)
    {
        var result = VerifikResultMapper.MapVehicle(
            Response(tieneGravamenes: tiene, prendas: prendas));
        Check(result, "gravamenes").Status.Should().Be("warn");
    }

    [Fact]
    public void Gravamenes_NoInfo_IsUnknown()
    {
        var result = VerifikResultMapper.MapVehicle(
            Response(tieneGravamenes: null, prendas: null));
        Check(result, "gravamenes").Status.Should().Be("unknown");
    }

    [Fact]
    public void Overall_AllOk_IsGreen()
    {
        var result = VerifikResultMapper.MapVehicle(Response());
        result.Overall.Should().Be("green");
    }

    [Fact]
    public void Overall_AnyFail_IsRed()
    {
        var result = VerifikResultMapper.MapVehicle(Response(soatEstado: "VENCIDO"));
        result.Overall.Should().Be("red");
    }

    [Fact]
    public void Overall_AnyWarnNoFail_IsYellow()
    {
        var result = VerifikResultMapper.MapVehicle(Response(tieneGravamenes: "SI"));
        result.Overall.Should().Be("yellow");
    }

    [Fact]
    public void HydratedFields_MapPlateVinAndYear()
    {
        var result = VerifikResultMapper.MapVehicle(
            Response(noPlaca: "XYZ987", noVin: "1HGCM82633A004352", modelo: "2021"));

        result.HydratedFields.Should().Contain(f => f.FieldKey == "plate" && f.ValueText == "XYZ987");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vin" && f.ValueText == "1HGCM82633A004352");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_year" && f.ValueText == "2021");
    }

    [Fact]
    public void Cilindraje_TypoTolerant_ByPlateUsesCilidraje()
    {
        // by-plate trae "cilidraje" (sin n); el normalizado debe resolverlo.
        var info = new VerifikInformacionGeneral { Cilidraje = "1600" };
        info.CilindrajeNormalizado.Should().Be("1600");
    }

    [Fact]
    public void Cilindraje_TypoTolerant_ByVinUsesCilindraje()
    {
        var info = new VerifikInformacionGeneral { Cilindraje = "2000" };
        info.CilindrajeNormalizado.Should().Be("2000");
    }

    [Fact]
    public void MapVehicle_NullData_DoesNotThrow_ProducesChecks()
    {
        var result = VerifikResultMapper.MapVehicle(new VerifikVehicleResponse());

        result.Provider.Should().Be("verifik");
        result.Checks.Should().HaveCount(4);
        result.HydratedFields.Should().BeEmpty();
    }

    [Fact]
    public void Overall_OkAndUnknownMix_IsGreen()
    {
        // estado unknown (null) + soat ok + tecno "NO APLICA" unknown + gravámenes ok.
        var response = new VerifikVehicleResponse
        {
            Data = new VerifikVehicleData
            {
                InformacionGeneral = new VerifikInformacionGeneral { TieneGravamenes = "NO", Prendas = "NO" }, // gravámenes ok, estado null → unknown
                Soat = [new VerifikSoat { Estado = "VIGENTE" }], // ok
                TecnoMecanica = [new VerifikTecnomecanica { Vigente = "NO APLICA" }], // unknown
            },
        };

        var result = VerifikResultMapper.MapVehicle(response);
        result.Overall.Should().Be("green"); // hay ok, ningún fail/warn
    }

    // Forma REAL de RUNT (datos ficticios, sin PII): vehículo ACTIVO, soat VIGENTE,
    // tecnoMecanica vacío, garantiasMobiliarias como ARRAY [], gravámenes en informacionGeneral.
    private const string RealShapeJson = """
        {
          "data": {
            "garantiasFavorDe": [],
            "garantiasMobiliarias": [],
            "limitacionPropiedad": [],
            "informacionGeneral": {
              "estadoDelVehiculo": "ACTIVO",
              "noPlaca": "QPL705",
              "noVin": "1HGCM82633A004352",
              "modelo": "2026",
              "cilindraje": "0",
              "tieneGravamenes": "NO",
              "prendas": "NO",
              "marca": "TESLA",
              "color": "PLATA"
            },
            "soat": [
              { "estado": "VIGENTE", "fechaVencimiento": "05/05/2027", "noPoliza": "12345" }
            ],
            "tecnoMecanica": [],
            "vin": "1HGCM82633A004352"
          },
          "signature": { "dateTime": "June 19, 2026 2:36 PM", "message": "Certified by Verifik.co" },
          "id": "ABC12"
        }
        """;

    [Fact]
    public void RealShape_DeserializesWithoutError_AndMapsCoherently()
    {
        // El bug original: garantiasMobiliarias array vs objeto rompía TODA la deserialización.
        var response = JsonSerializer.Deserialize<VerifikVehicleResponse>(RealShapeJson, WebJsonOptions);

        response.Should().NotBeNull();
        response!.Data.Should().NotBeNull();
        response.Data!.InformacionGeneral!.EstadoDelVehiculo.Should().Be("ACTIVO");
        response.Data.Soat.Should().HaveCount(1);
        response.Data.TecnoMecanica.Should().BeEmpty();
        response.Data.GarantiasMobiliarias.Should().BeEmpty(); // array vacío, no rompe

        var result = VerifikResultMapper.MapVehicle(response);

        Check(result, "estado_vehiculo").Status.Should().Be("ok");
        Check(result, "soat").Status.Should().Be("ok");
        Check(result, "tecnomecanica").Status.Should().Be("unknown"); // tecnoMecanica vacío
        Check(result, "gravamenes").Status.Should().Be("ok");
        result.Overall.Should().Be("green"); // hay ok, ningún fail/warn

        result.HydratedFields.Should().Contain(f => f.FieldKey == "plate" && f.ValueText == "QPL705");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vin" && f.ValueText == "1HGCM82633A004352");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_year" && f.ValueText == "2026");
    }
}

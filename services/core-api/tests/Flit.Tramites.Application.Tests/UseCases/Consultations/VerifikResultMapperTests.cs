using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

public sealed class VerifikResultMapperTests
{
    private static VerifikVehicleResponse Response(
        string? estado = "ACTIVO",
        string? soatEstado = "VIGENTE",
        string? tecnoVigente = "SI",
        string? tieneGravamenes = "NO",
        string? prendas = "NO",
        string? limitacion = null,
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
                },
                Soat = soatEstado is null ? [] : [new VerifikSoat { Estado = soatEstado }],
                RevisionTecnomecanica = new VerifikTecnomecanica { Vigente = tecnoVigente },
                GarantiasMobiliarias = new VerifikGravamenes
                {
                    TieneGravamenes = tieneGravamenes,
                    Prendas = prendas,
                    LimitacionPropiedad = limitacion,
                },
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
    public void Soat_EmptyList_IsFail()
    {
        var result = VerifikResultMapper.MapVehicle(Response(soatEstado: null));
        Check(result, "soat").Status.Should().Be("fail");
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
    public void Gravamenes_NoneIsOk()
    {
        var result = VerifikResultMapper.MapVehicle(
            Response(tieneGravamenes: "NO", prendas: "NO", limitacion: null));
        Check(result, "gravamenes").Status.Should().Be("ok");
    }

    [Theory]
    [InlineData("SI", "NO", null)]
    [InlineData("NO", "SI", null)]
    [InlineData("NO", "NO", "EMBARGO")]
    public void Gravamenes_AnyPresentIsWarn(string tiene, string prendas, string? limitacion)
    {
        var result = VerifikResultMapper.MapVehicle(
            Response(tieneGravamenes: tiene, prendas: prendas, limitacion: limitacion));
        Check(result, "gravamenes").Status.Should().Be("warn");
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
                InformacionGeneral = new VerifikInformacionGeneral(), // estado null → unknown
                Soat = [new VerifikSoat { Estado = "VIGENTE" }], // ok
                RevisionTecnomecanica = new VerifikTecnomecanica { Vigente = "NO APLICA" }, // unknown
                GarantiasMobiliarias = new VerifikGravamenes { TieneGravamenes = "NO", Prendas = "NO" }, // ok
            },
        };

        var result = VerifikResultMapper.MapVehicle(response);
        result.Overall.Should().Be("green"); // hay ok, ningún fail/warn
    }
}

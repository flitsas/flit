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
        string? cilidraje = null,
        string? marca = "TESLA",
        string? linea = "MODEL 3",
        string? color = "PLATA",
        string? claseVehiculo = "AUTOMOVIL",
        string? tipoCombustible = "ELECTRICO",
        string? organismoTransito = "STT BOGOTA") =>
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
                    Marca = marca,
                    Linea = linea,
                    Color = color,
                    ClaseVehiculo = claseVehiculo,
                    TipoCombustible = tipoCombustible,
                    OrganismoTransito = organismoTransito,
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
    public void HydratedFields_MapFullVehicleAttributes()
    {
        var response = Response(
            estado: "ACTIVO",
            cilindraje: "1991",
            marca: "TESLA",
            linea: "MODEL 3",
            color: "PLATA",
            claseVehiculo: "AUTOMOVIL",
            tipoCombustible: "ELECTRICO",
            organismoTransito: "STT BOGOTA");

        var result = VerifikResultMapper.MapVehicle(response);

        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_brand" && f.ValueText == "TESLA");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_line" && f.ValueText == "MODEL 3");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_color" && f.ValueText == "PLATA");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_class" && f.ValueText == "AUTOMOVIL");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_fuel" && f.ValueText == "ELECTRICO");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_engine_displacement" && f.ValueText == "1991");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "transit_office_name" && f.ValueText == "STT BOGOTA");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_state" && f.ValueText == "ACTIVO");
    }

    [Fact]
    public void HydratedFields_OmitsBlankAttributes()
    {
        // Atributos en blanco no deben generar field_values vacíos.
        var response = Response(
            marca: null,
            linea: "",
            color: "   ",
            claseVehiculo: null,
            tipoCombustible: null,
            organismoTransito: null);

        var result = VerifikResultMapper.MapVehicle(response);

        result.HydratedFields.Should().NotContain(f => f.FieldKey == "vehicle_brand");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "vehicle_line");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "vehicle_color");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "vehicle_class");
    }

    [Fact]
    public void HydratedFields_EngineDisplacement_TypoTolerant_UsesCilidraje()
    {
        // by-plate trae "cilidraje" (typo); el field_value debe resolverlo igual.
        var response = Response(cilindraje: null, cilidraje: "1600");

        var result = VerifikResultMapper.MapVehicle(response);

        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_engine_displacement" && f.ValueText == "1600");
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
              {
                "estado": "VIGENTE",
                "fechaVencimiento": "05/05/2027",
                "noPoliza": "12345",
                "fechaExpedicion": "04/05/2026",
                "fechaVigencia": "06/05/2026",
                "entidadExpideSoat": "SEGUROS DEL ESTADO S.A."
              }
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

        // HU #11134 — las seis celdas del bloque SOAT del certificado salen del RUNT. Antes, tres de
        // ellas solo se llenaban con el OCR del PDF del SOAT: sin ese documento salían en blanco.
        result.HydratedFields.Should().Contain(f => f.FieldKey == "soat_poliza" && f.ValueText == "12345");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "soat_expedicion" && f.ValueText == "04/05/2026");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "soat_vigencia" && f.ValueText == "06/05/2026");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "soat_vencimiento" && f.ValueText == "05/05/2027");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "soat_aseguradora" && f.ValueText == "SEGUROS DEL ESTADO S.A.");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "soat_estado");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vin" && f.ValueText == "1HGCM82633A004352");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "vehicle_year" && f.ValueText == "2026");
    }

    [Fact]
    public void Soat_SinPolizaNiFechas_NoEscribeEsasLlaves()
    {
        // Regla del negocio: lo que no vino en la consulta se deja EN BLANCO, no se rellena. Y al no
        // escribirse la llave, el OCR del PDF puede aportarla después como respaldo.
        var result = VerifikResultMapper.MapVehicle(Response(soatEstado: "VIGENTE"));

        result.HydratedFields.Should().NotContain(f => f.FieldKey == "soat_poliza");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "soat_expedicion");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "soat_vigencia");
    }

    // ── HU #11135 — RTM: lectura tolerante de los campos sin contrato confirmado ─────────────────

    private static ConsultationResult MapearRtm(string rtmJson)
    {
        var json = $$"""
            {
              "data": {
                "informacionGeneral": { "noPlaca": "QPL705", "estadoDelVehiculo": "ACTIVO" },
                "soat": [],
                "tecnoMecanica": [ {{rtmJson}} ]
              }
            }
            """;
        var response = JsonSerializer.Deserialize<VerifikVehicleResponse>(json, WebJsonOptions);
        return VerifikResultMapper.MapVehicle(response!);
    }

    [Fact]
    public void Rtm_NumeroYFechasDeLaRevision_YaNoSeAdivinanPorNombresCandidatos()
    {
        // HU #11303 — se retiró la resolución por nombres candidatos que introdujo la HU #11135. No es
        // una decisión de estilo: la medición en base de datos mostró CERO filas de rtm_numero y
        // rtm_expedicion en todo el ambiente, así que la lista nunca acertó un nombre. Lo único que
        // producía era cobertura aparente sobre un hueco real, que es el mecanismo que originó el
        // Feature #11301. La evidencia de qué manda el proveedor vive ahora en el payload crudo.
        var result = MapearRtm("""
            { "vigente": "SI", "cdaExpide": "CDA NORTE", "noCertificado": "CDA-99887", "fechaExpedicion": "31/01/2026" }
            """);

        result.HydratedFields.Should().NotContain(f => f.FieldKey == "rtm_numero");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "rtm_expedicion");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "rtm_vigencia");

        // Lo que el modelo SÍ declara se sigue leyendo igual.
        result.HydratedFields.Should().Contain(f => f.FieldKey == "rtm_entidad" && f.ValueText == "CDA NORTE");
    }

    [Fact]
    public void Rtm_SinNingunNombreConocido_NoEscribeLaLlave()
    {
        // Celda en blanco y el OCR del PDF puede aportarla: nunca se inventa un dato del certificado.
        var result = MapearRtm("""{ "vigente": "SI", "campoDesconocido": "X" }""");

        result.HydratedFields.Should().NotContain(f => f.FieldKey == "rtm_numero");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "rtm_vigencia");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "rtm_expedicion");
    }

    [Fact]
    public void Rtm_CamposNoModelados_SeConservanEnVezDeDescartarse()
    {
        // La red de seguridad: lo que el proveedor manda y el modelo no declara deja de perderse en
        // silencio. Es lo que permite descubrir el contrato real sin volver a adivinar.
        var json = """
            {"data":{"informacionGeneral":{"noPlaca":"QPL705"},"soat":[],
             "tecnoMecanica":[{"vigente":"SI","campoNuevoDelProveedor":"valor"}]}}
            """;

        var response = JsonSerializer.Deserialize<VerifikVehicleResponse>(json, WebJsonOptions);

        response!.Data!.TecnoMecanica![0].CamposNoModelados.Should().ContainKey("campoNuevoDelProveedor");
    }
}

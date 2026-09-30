using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// Bug #13203 — garantía mobiliaria registrada en el RNGM con banderas «NO». Kyverum la trae en
/// <c>data.garantiasPrendas</c> y Verifik en <c>data.garantiasMobiliarias</c>, con el MISMO shape
/// (<c>entidad</c>, <c>numeroDocumentoEntidad</c>, <c>tipoDocumentoEntidad</c>, <c>fechaRegistro</c>,
/// <c>estado</c>, ids como string). Datos ficticios.
/// Uso de ejemplo:
/// <code>KyverumRuntVehicleResultMapper.MapVehicle(JsonSerializer.Deserialize&lt;KyverumRuntVehicleResponse&gt;(json)!)</code>
/// </summary>
public sealed class RuntGarantiasMobiliariasBug13203Tests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private const string ItemRngm = """
        {
          "idPrenda": "1000001",
          "idVehiculoPrenda": "2000002",
          "fechaRegistro": "30/09/2026",
          "tipoDocumentoEntidad": "NIT",
          "numeroDocumentoEntidad": "900000001",
          "entidad": "BANCO DE PRUEBA S.A.",
          "estado": "Registro de la garantía en el RNGM por parte de RUNT"
        }
        """;

    private static string KyverumJson(string gravamenes = "NO", string prendas = "NO", string garantiasPrendas = "[" + ItemRngm + "]") => $$"""
        {
          "ok": true,
          "data": {
            "vehiculo": { "placa": "PRU13A", "estadoAutomotor": "ACTIVO", "gravamenes": "{{gravamenes}}", "prendas": "{{prendas}}" },
            "garantias": [],
            "garantiasPrendas": {{garantiasPrendas}}
          }
        }
        """;

    private static string VerifikJson(string? tieneGravamenes = "SI", string? prendas = "NO", string garantias = "[" + ItemRngm + "]")
    {
        static string J(string? v) => v is null ? "null" : $"\"{v}\"";
        return $$"""
        {
          "data": {
            "informacionGeneral": {
              "noPlaca": "PRU13A", "estadoDelVehiculo": "ACTIVO",
              "tieneGravamenes": {{J(tieneGravamenes)}}, "prendas": {{J(prendas)}}
            },
            "soat": [],
            "tecnoMecanica": [],
            "garantiasMobiliarias": {{garantias}}
          }
        }
        """;
    }

    private static ConsultationCheck Check(ConsultationResult r) => r.Checks.Single(c => c.Key == "gravamenes");

    private static string? Text(ConsultationResult r, string key) =>
        r.HydratedFields.FirstOrDefault(f => f.FieldKey == key)?.ValueText;

    private static JsonElement PrimerGravamen(ConsultationResult r)
    {
        var json = r.HydratedFields.Single(f => f.FieldKey == "runt_gravamenes").ValueJson;
        using var doc = JsonDocument.Parse(json!);
        doc.RootElement.GetArrayLength().Should().Be(1);
        return doc.RootElement[0].Clone();
    }

    private static void DebeSerElAcreedorNormalizado(JsonElement g)
    {
        g.GetProperty("nombreAcreedor").GetString().Should().Be("BANCO DE PRUEBA S.A.");
        g.GetProperty("numeroDocumentoAcreedor").GetString().Should().Be("900000001");
        g.GetProperty("tipoDocumentoAcreedor").GetString().Should().Be("NIT");
        g.GetProperty("fechaInscripcion").GetString().Should().Be("30/09/2026");
        g.GetProperty("estadoPrenda").GetString().Should().Be("Registro de la garantía en el RNGM por parte de RUNT");
        g.GetProperty("idPrenda").ToString().Should().Be("1000001");
        g.GetProperty("idVehiculoPrenda").ToString().Should().Be("2000002");
        g.TryGetProperty("entidad", out _).Should().BeFalse("la salida es el contrato normalizado, no el crudo");
    }

    // ── Caso A: Kyverum, banderas NO + 1 garantía en garantiasPrendas ────────────────────────────
    [Theory]
    [InlineData(true)]
    [InlineData(false)] // sin opciones Web: los ids string no pueden depender de NumberHandling
    public void Kyverum_BanderasNo_ConGarantiaRngm_WarnYDetalleNormalizado(bool web)
    {
        var response = web
            ? JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(KyverumJson(), WebJsonOptions)!
            : JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(KyverumJson())!;

        var result = KyverumRuntVehicleResultMapper.MapVehicle(response);

        Check(result).Status.Should().Be("warn");
        Check(result).Message.Should().Be("El RUNT registra 1 garantía(s) mobiliaria(s) (gravámenes: NO · prendas: NO)");
        Text(result, "runt_nombre_acreedor").Should().Be("BANCO DE PRUEBA S.A.");
        DebeSerElAcreedorNormalizado(PrimerGravamen(result));
    }

    [Fact]
    public void Kyverum_IdPrendaNumerico_SeAcepta()
    {
        var json = KyverumJson(garantiasPrendas: """[{ "idPrenda": 1000001, "idVehiculoPrenda": 2000002, "entidad": "BANCO DE PRUEBA S.A." }]""");
        var result = KyverumRuntVehicleResultMapper.MapVehicle(JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(json)!);

        var g = PrimerGravamen(result);
        g.GetProperty("idPrenda").ToString().Should().Be("1000001");
        g.GetProperty("idVehiculoPrenda").ToString().Should().Be("2000002");
    }

    [Fact]
    public void Kyverum_BanderaSiConTilde_EsWarnConElMensajeDeBanderas()
    {
        var result = KyverumRuntVehicleResultMapper.MapVehicle(
            JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(KyverumJson(gravamenes: "SÍ", garantiasPrendas: "[]"), WebJsonOptions)!);

        Check(result).Status.Should().Be("warn");
        Check(result).Message.Should().StartWith("El vehículo tiene gravámenes o prendas");
    }

    [Fact]
    public void Kyverum_ItemVacio_SeFiltraYNoInventaGravamen()
    {
        var result = KyverumRuntVehicleResultMapper.MapVehicle(
            JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(KyverumJson(garantiasPrendas: "[{}]"), WebJsonOptions)!);

        Check(result).Status.Should().Be("ok");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "runt_gravamenes");
    }

    // ── Caso B: Verifik, tieneGravamenes SI + 1 garantía cruda ───────────────────────────────────
    [Fact]
    public void Verifik_GarantiaRngm_SeNormalizaYReconoceAlAcreedor()
    {
        var response = JsonSerializer.Deserialize<VerifikVehicleResponse>(VerifikJson(), WebJsonOptions)!;

        var result = VerifikResultMapper.MapVehicle(response);

        Check(result).Status.Should().Be("warn");
        Check(result).Message.Should().StartWith("El vehículo tiene gravámenes o prendas");
        Text(result, "runt_nombre_acreedor").Should().Be("BANCO DE PRUEBA S.A.");
        DebeSerElAcreedorNormalizado(PrimerGravamen(result));
    }

    [Fact]
    public void Verifik_BanderasNo_ConGarantia_EsWarnPorGarantias()
    {
        var result = VerifikResultMapper.MapVehicle(
            JsonSerializer.Deserialize<VerifikVehicleResponse>(VerifikJson(tieneGravamenes: "NO"))!);

        Check(result).Status.Should().Be("warn");
        Check(result).Message.Should().Be("El RUNT registra 1 garantía(s) mobiliaria(s) (gravámenes: NO · prendas: NO)");
    }

    [Fact]
    public void Verifik_SinBanderas_ConGarantia_EsWarnConGuiones()
    {
        var result = VerifikResultMapper.MapVehicle(
            JsonSerializer.Deserialize<VerifikVehicleResponse>(VerifikJson(tieneGravamenes: null, prendas: null), WebJsonOptions)!);

        Check(result).Status.Should().Be("warn");
        Check(result).Message.Should().Be("El RUNT registra 1 garantía(s) mobiliaria(s) (gravámenes: — · prendas: —)");
    }

    [Fact]
    public void Verifik_SinBanderasNiGarantias_EsUnknown()
    {
        var result = VerifikResultMapper.MapVehicle(
            JsonSerializer.Deserialize<VerifikVehicleResponse>(VerifikJson(tieneGravamenes: null, prendas: null, garantias: "[]"), WebJsonOptions)!);

        Check(result).Status.Should().Be("unknown");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "runt_gravamenes");
    }

    [Fact]
    public void Verifik_BanderaSiConTilde_EsWarn()
    {
        var result = VerifikResultMapper.MapVehicle(
            JsonSerializer.Deserialize<VerifikVehicleResponse>(VerifikJson(tieneGravamenes: "NO", prendas: "SÍ", garantias: "[]"), WebJsonOptions)!);

        Check(result).Status.Should().Be("warn");
    }
}

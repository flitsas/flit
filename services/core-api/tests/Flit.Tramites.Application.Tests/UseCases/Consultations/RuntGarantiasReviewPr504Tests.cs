using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// Bug #13203 — ajustes de la revisión del PR #504.
/// <list type="bullet">
/// <item>B1: cuando el proveedor responde, las claves de la señal de gravamen se escriben SIEMPRE
/// (<c>runt_gravamenes = "[]"</c>, <c>runt_nombre_acreedor</c>/<c>runt_tiene_*</c> null) para que el
/// upsert pise lo que dejó una consulta anterior.</item>
/// <item>L1: un tipo inesperado en <c>garantiasMobiliarias</c>/<c>garantiasPrendas</c> no tumba la consulta.</item>
/// </list>
/// Datos ficticios.
/// Uso de ejemplo:
/// <code>VerifikResultMapper.MapVehicle(JsonSerializer.Deserialize&lt;VerifikVehicleResponse&gt;(json)!)</code>
/// </summary>
public sealed class RuntGarantiasReviewPr504Tests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private static HydratedField? Field(ConsultationResult r, string key) =>
        r.HydratedFields.SingleOrDefault(f => f.FieldKey == key);

    private static void DebeEscribirLaSenalVacia(ConsultationResult result)
    {
        var detalle = Field(result, "runt_gravamenes");
        detalle.Should().NotBeNull("sin la clave el upsert conserva el detalle de la consulta anterior");
        detalle!.ValueJson.Should().Be("[]");
        detalle.ValueText.Should().BeNull();

        var acreedor = Field(result, "runt_nombre_acreedor");
        acreedor.Should().NotBeNull();
        acreedor!.ValueText.Should().BeNull();
        acreedor.ValueJson.Should().BeNull();
    }

    private static string VerifikJson(string garantias, string? gravamenes = "NO", string? prendas = "NO")
    {
        static string J(string? v) => v is null ? "null" : $"\"{v}\"";
        return $$"""
        {
          "data": {
            "informacionGeneral": { "noPlaca": "PRU13A", "estadoDelVehiculo": "ACTIVO",
                                    "tieneGravamenes": {{J(gravamenes)}}, "prendas": {{J(prendas)}} },
            "soat": [], "tecnoMecanica": [],
            "garantiasMobiliarias": {{garantias}}
          }
        }
        """;
    }

    private static string KyverumJson(string garantiasPrendas) => $$"""
        {
          "ok": true,
          "data": {
            "vehiculo": { "placa": "PRU13A", "estadoAutomotor": "ACTIVO", "gravamenes": "NO", "prendas": "NO" },
            "garantias": [],
            "garantiasPrendas": {{garantiasPrendas}}
          }
        }
        """;

    // ── B1: la señal se escribe siempre ──────────────────────────────────────────────────────

    [Fact]
    public void Kyverum_SinGarantias_EscribeLaSenalVacia()
    {
        var result = KyverumRuntVehicleResultMapper.MapVehicle(
            JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(KyverumJson("[]"), WebJsonOptions)!);

        DebeEscribirLaSenalVacia(result);
        Field(result, "runt_tiene_gravamenes")!.ValueText.Should().Be("NO");
        Field(result, "runt_tiene_prendas")!.ValueText.Should().Be("NO");
    }

    [Fact]
    public void Verifik_SinGarantiasNiBanderas_EscribeLaSenalVaciaYBanderasNull()
    {
        var result = VerifikResultMapper.MapVehicle(
            JsonSerializer.Deserialize<VerifikVehicleResponse>(VerifikJson("[]", gravamenes: null, prendas: null), WebJsonOptions)!);

        DebeEscribirLaSenalVacia(result);
        Field(result, "runt_tiene_gravamenes").Should().NotBeNull("una bandera SI vieja debe quedar pisada");
        Field(result, "runt_tiene_gravamenes")!.ValueText.Should().BeNull();
        Field(result, "runt_tiene_prendas").Should().NotBeNull();
        Field(result, "runt_tiene_prendas")!.ValueText.Should().BeNull();
    }

    [Fact]
    public void Intempo_SinDetalle_EscribeLaSenalVaciaYBanderasNull()
    {
        var result = IntempoVehicleResultMapper.Map(new IntempoVehicleResponse
        {
            NoPlaca = "PRU13A",
            EstadoDelVehiculo = "ACTIVO",
        });

        DebeEscribirLaSenalVacia(result);
        Field(result, "runt_tiene_gravamenes").Should().NotBeNull();
        Field(result, "runt_tiene_gravamenes")!.ValueText.Should().BeNull();
        Field(result, "runt_tiene_prendas").Should().NotBeNull();
        Field(result, "runt_tiene_prendas")!.ValueText.Should().BeNull();
    }

    // ── L1: tipos inesperados no tumban la consulta ──────────────────────────────────────────

    [Fact]
    public void Verifik_DocumentoNumerico_SeLeeComoString()
    {
        var json = VerifikJson("""[ { "entidad": "BANCO DE PRUEBA S.A.", "numeroDocumentoEntidad": 900000001, "idPrenda": 1000001 } ]""");

        var result = VerifikResultMapper.MapVehicle(JsonSerializer.Deserialize<VerifikVehicleResponse>(json)!);

        Field(result, "runt_gravamenes")!.ValueJson.Should().Contain("\"numeroDocumentoAcreedor\":\"900000001\"");
        Field(result, "runt_nombre_acreedor")!.ValueText.Should().Be("BANCO DE PRUEBA S.A.");
    }

    [Theory]
    [InlineData("""[ null, 5, "texto", true, [1], { "entidad": "BANCO DE PRUEBA S.A." } ]""")]
    public void Verifik_ItemsNoObjeto_SeDescartan(string garantias)
    {
        var result = VerifikResultMapper.MapVehicle(
            JsonSerializer.Deserialize<VerifikVehicleResponse>(VerifikJson(garantias), WebJsonOptions)!);

        using var doc = JsonDocument.Parse(Field(result, "runt_gravamenes")!.ValueJson!);
        doc.RootElement.GetArrayLength().Should().Be(1);
        result.Checks.Single(c => c.Key == "gravamenes").Message.Should().Contain("1 garantía(s)");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("""{ "a": 1 }""")]
    [InlineData("[1, 2]")]
    public void IdConTipoInesperado_QuedaNullSinRomper(string id)
    {
        var json = KyverumJson($$"""[ { "idPrenda": {{id}}, "idVehiculoPrenda": {{id}}, "entidad": "BANCO DE PRUEBA S.A.", "numeroDocumentoEntidad": {{id}} } ]""");

        var result = KyverumRuntVehicleResultMapper.MapVehicle(JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(json)!);

        using var doc = JsonDocument.Parse(Field(result, "runt_gravamenes")!.ValueJson!);
        var g = doc.RootElement.EnumerateArray().Single();
        g.TryGetProperty("idPrenda", out _).Should().BeFalse();
        g.TryGetProperty("idVehiculoPrenda", out _).Should().BeFalse();
        g.TryGetProperty("numeroDocumentoAcreedor", out _).Should().BeFalse();
        g.GetProperty("nombreAcreedor").GetString().Should().Be("BANCO DE PRUEBA S.A.");
    }

    [Fact]
    public void Kyverum_DocumentoNumericoEItemsEscalares_NoRompen()
    {
        var json = KyverumJson("""[ 7, null, { "entidad": "BANCO DE PRUEBA S.A.", "numeroDocumentoEntidad": 900000001 } ]""");

        var result = KyverumRuntVehicleResultMapper.MapVehicle(JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(json)!);

        Field(result, "runt_gravamenes")!.ValueJson.Should().Contain("\"numeroDocumentoAcreedor\":\"900000001\"");
    }

    [Fact]
    public void Verifik_GarantiasComoObjeto_NoRompeLaConsulta()
    {
        var result = VerifikResultMapper.MapVehicle(
            JsonSerializer.Deserialize<VerifikVehicleResponse>(VerifikJson("""{ "entidad": "X" }"""), WebJsonOptions)!);

        result.Checks.Single(c => c.Key == "gravamenes").Status.Should().Be("ok");
        Field(result, "runt_gravamenes")!.ValueJson.Should().Be("[]");
    }
}

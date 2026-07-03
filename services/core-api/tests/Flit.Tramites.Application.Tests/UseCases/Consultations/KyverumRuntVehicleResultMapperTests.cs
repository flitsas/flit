using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #10478 — mapper Kyverum RUNT vehículo, validado contra los fixtures reales anonimizados
/// (<c>context/reference/kyverum-runt/</c>). Verifica el contrato Kyverum-first: mismos Check.Key e
/// HydratedField.FieldKey que Verifik, con Source = <c>kyverum_runt</c>.
/// </summary>
public sealed class KyverumRuntVehicleResultMapperTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private static KyverumRuntVehicleResponse Load(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Consultations", "Fixtures", "KyverumRunt", fixture);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(json, WebJsonOptions)!;
    }

    private static string? Status(ConsultationResult r, string key) =>
        r.Checks.FirstOrDefault(c => c.Key == key)?.Status;

    private static string? Field(ConsultationResult r, string key) =>
        r.HydratedFields.FirstOrDefault(f => f.FieldKey == key)?.ValueText;

    // ── VIN Tesla QYQ132 — matrícula inicial, SOAT vigente, RTM ausente, prendas SI ──────────
    [Fact]
    public void VinTesla_ChecksYHydratacionSegunContrato()
    {
        var result = KyverumRuntVehicleResultMapper.MapVehicle(Load("vehicle-vin-tesla-qyq132.json"));

        result.Provider.Should().Be("kyverum_runt");
        result.Checks.Should().OnlyContain(c => c.Source == "kyverum_runt");

        Status(result, "estado_vehiculo").Should().Be("ok");
        Status(result, "soat").Should().Be("ok");
        Status(result, "tecnomecanica").Should().Be("unknown");   // rtm vacío → unknown (no fail)
        Status(result, "gravamenes").Should().Be("warn");         // prendas SI

        // Gravámenes warn ⇒ overall yellow.
        result.Overall.Should().Be("yellow");

        Field(result, "plate").Should().Be("QYQ132");
        Field(result, "vin").Should().Be("LRWYGCFJ5TC576828");
        Field(result, "vehicle_brand").Should().Be("TESLA");
        Field(result, "vehicle_line").Should().Be("MODEL Y");
        Field(result, "vehicle_year").Should().Be("2026");
        Field(result, "vehicle_fuel").Should().Be("ELECTRICO");
        Field(result, "vehicle_state").Should().Be("ACTIVO");
        Field(result, "soat_aseguradora").Should().Be("LA PREVISORA S.A.COMPAÑIA DE SEGUROS");
    }

    // ── Placa Yamaha JNH38H — traspaso, SOAT múltiple (1 VIGENTE + 1 NO VIGENTE), sin gravámenes ─
    [Fact]
    public void PlacaYamaha_SoatMultiple_PrefiereVigenteYOverallVerde()
    {
        var result = KyverumRuntVehicleResultMapper.MapVehicle(Load("vehicle-plate-yamaha-jnh38h.json"));

        Status(result, "estado_vehiculo").Should().Be("ok");
        Status(result, "soat").Should().Be("ok");               // AXA VIGENTE gana a Previsora NO VIGENTE
        Status(result, "tecnomecanica").Should().Be("unknown");
        Status(result, "gravamenes").Should().Be("ok");         // gravamenes NO + prendas NO

        result.Overall.Should().Be("green");

        Field(result, "vehicle_engine_number").Should().Be("G3K6E0042405");
        Field(result, "vehicle_engine_displacement").Should().Be("149");
        // Hidrata la póliza VIGENTE (AXA), no la vencida (Previsora).
        Field(result, "soat_aseguradora").Should().Be("AXA COLPATRIA SEGUROS SA");
        Field(result, "soat_vencimiento").Should().Be("2027-01-23T00:00:00.000-05:00");
    }

    // ── Robustez: nulls/listas vacías nunca lanzan ──────────────────────────────────────────
    [Fact]
    public void RespuestaVacia_NoLanza_YProduceChecksUnknown()
    {
        var result = KyverumRuntVehicleResultMapper.MapVehicle(new KyverumRuntVehicleResponse { Ok = true });

        result.Provider.Should().Be("kyverum_runt");
        Status(result, "estado_vehiculo").Should().Be("unknown");
        Status(result, "soat").Should().Be("unknown");
        Status(result, "gravamenes").Should().Be("unknown");
        result.HydratedFields.Should().BeEmpty();
    }
}

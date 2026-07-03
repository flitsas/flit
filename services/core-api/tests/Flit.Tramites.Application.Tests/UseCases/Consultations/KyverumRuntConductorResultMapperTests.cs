using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #10478 — mapper Kyverum RUNT conductor, validado contra el fixture real anonimizado
/// (persona CC 1193552679: licencia ACTIVA, multas SI). Contrato Kyverum-first: mismos Check.Key e
/// HydratedField.FieldKey que <c>verifik_conductor</c>, con Source = <c>kyverum_runt_conductor</c>.
/// </summary>
public sealed class KyverumRuntConductorResultMapperTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private static KyverumRuntPersonaResponse Load(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Consultations", "Fixtures", "KyverumRunt", fixture);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<KyverumRuntPersonaResponse>(json, WebJsonOptions)!;
    }

    private static string? Status(ConsultationResult r, string key) =>
        r.Checks.FirstOrDefault(c => c.Key == key)?.Status;

    private static string? Field(ConsultationResult r, string key) =>
        r.HydratedFields.FirstOrDefault(f => f.FieldKey == key)?.ValueText;

    [Fact]
    public void PersonaDaniel_LicenciaActivaConMultas_MultasFailYOverallNoVerde()
    {
        var result = KyverumRuntConductorResultMapper.Map(Load("persona-daniel-1193552679.json"));

        result.Provider.Should().Be("kyverum_runt_conductor");
        result.Checks.Should().OnlyContain(c => c.Source == "kyverum_runt_conductor");

        Status(result, "conductor_identidad").Should().Be("ok");
        Status(result, "conductor_estado").Should().Be("ok");     // ACTIVO + ACTIVA
        Status(result, "conductor_licencia").Should().Be("ok");   // licencia ACTIVA
        Status(result, "conductor_multas").Should().Be("fail");   // tieneMultas SI

        // Multas SI ⇒ no verde.
        result.Overall.Should().Be("yellow");

        // nombres="DANIEL " + apellidos="AMADO GARCIA" → colapsa espacios.
        Field(result, "person_full_name").Should().Be("DANIEL AMADO GARCIA");
        Field(result, "person_first_name").Should().Be("DANIEL");
        Field(result, "person_last_name").Should().Be("AMADO GARCIA");
        Field(result, "person_license_status").Should().Be("ACTIVO");
        Field(result, "person_citizen_status").Should().Be("ACTIVA");
        Field(result, "person_has_pending_fines").Should().Be("true");
        Field(result, "person_has_active_license").Should().Be("true");
        Field(result, "person_license_categories").Should().Be("A2,C1,B1");
    }

    [Fact]
    public void PersonaNoEncontrada_SinNombre_IdentidadUnknownYSinHidratacion()
    {
        var result = KyverumRuntConductorResultMapper.Map(new KyverumRuntPersonaResponse { Ok = false });

        result.Provider.Should().Be("kyverum_runt_conductor");
        Status(result, "conductor_identidad").Should().Be("unknown");
        result.Checks.Should().ContainSingle();   // solo identidad cuando no hay persona
        result.HydratedFields.Should().BeEmpty();
        result.Overall.Should().Be("yellow");
    }
}

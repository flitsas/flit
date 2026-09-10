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

        // El nombre sale de `identidad`, no de persona.nombres/apellidos (que llegan enmascarados).
        Field(result, "person_full_name").Should().Be("DANIEL AMADO GARCIA");
        Field(result, "person_first_name").Should().Be("DANIEL");
        Field(result, "person_first_last_name").Should().Be("AMADO");
        Field(result, "person_second_last_name").Should().Be("GARCIA");
        Field(result, "person_last_name").Should().Be("AMADO GARCIA");
        Field(result, "person_second_name").Should().BeNull();   // persona con un solo nombre
        Field(result, "person_license_status").Should().Be("ACTIVO");
        Field(result, "person_citizen_status").Should().Be("ACTIVA");
        Field(result, "person_has_pending_fines").Should().Be("true");
        Field(result, "person_has_active_license").Should().Be("true");
        Field(result, "person_license_categories").Should().Be("A2,C1,B1");
    }

    // El RUNT enmascara persona.nombres/apellidos. Si alguien vuelve a leerlos, el nombre del actor
    // termina en basura ("SL CS GZ" tras el saneo del front), así que se blinda explícitamente.
    [Fact]
    public void PersonaConNombresEnmascarados_NingunCampoHidratadoTraeAsteriscos()
    {
        var result = KyverumRuntConductorResultMapper.Map(Load("persona-daniel-1193552679.json"));

        result.HydratedFields.Should().NotContain(f => f.ValueText != null && f.ValueText.Contains('*'));
    }

    // Sin bloque `identidad` (respuestas que solo traen la persona), el nombre real sigue estando en
    // los campos desglosados de `persona` — nunca en los enmascarados.
    [Fact]
    public void SinBloqueIdentidad_UsaLosDesglosadosDePersona()
    {
        var result = KyverumRuntConductorResultMapper.Map(new KyverumRuntPersonaResponse
        {
            Ok = true,
            Persona = new KyverumRuntPersona
            {
                Nombres = "J****E G****L J****E",
                Apellidos = "A****A M****D",
                PrimerNombre = "JOSE",
                SegundoNombre = "GABRIEL JAIME",
                PrimerApellido = "ACOSTA",
                SegundoApellido = "MADRID",
                NombreCompleto = "JOSE GABRIEL JAIME ACOSTA MADRID",
                EstadoPersona = "ACTIVA",
                EstadoConductor = "ACTIVO",
            },
        });

        Field(result, "person_full_name").Should().Be("JOSE GABRIEL JAIME ACOSTA MADRID");
        Field(result, "person_first_name").Should().Be("JOSE");
        Field(result, "person_second_name").Should().Be("GABRIEL JAIME");
        Field(result, "person_first_last_name").Should().Be("ACOSTA");
        Field(result, "person_second_last_name").Should().Be("MADRID");
    }

    // Solo quedan los campos enmascarados: no hay nombre utilizable, así que la persona se reporta
    // como NO hallada y el wizard cae al ingreso manual, en vez de autopoblar asteriscos.
    [Fact]
    public void SoloCamposEnmascarados_SeReportaComoNoHallada()
    {
        var result = KyverumRuntConductorResultMapper.Map(new KyverumRuntPersonaResponse
        {
            Ok = true,
            Persona = new KyverumRuntPersona
            {
                Nombres = "S****L",
                Apellidos = "C****S G****Z",
                EstadoPersona = "ACTIVA",
                EstadoConductor = "ACTIVO",
            },
        });

        Status(result, "conductor_identidad").Should().Be("unknown");
        result.HydratedFields.Should().BeEmpty();
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

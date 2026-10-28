using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Simulacion;
using Flit.Consultas.Grpc.V1;
using Flit.Infrastructure.Consultations;
using Flit.Platform.Grpc.V1;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// HU #14153 AC3 — en simulación (solo LOCAL y DEV) ConsultarConductor y ConsultarRues traen <c>respuesta_cruda</c> cuando se pide
/// <c>incluir_respuesta_cruda</c> (lo pide Diagnóstico para runt_propietario, runt_locatario y rues), armada con fixtures FICTICIOS con
/// la forma del JSON real del proveedor: el conductor de Kyverum (<see cref="KyverumRuntPersonaResponse"/>) con una licencia activa,
/// una vencida y una suspendida, con sus categorías y vencimientos; la empresa de Verifik RUES (<see cref="VerifikRuesResponse"/>) con
/// representantes legales y actividades CIIU. Sin la opción no hay crudo (como el servicio real). Ningún fixture trae documentos, NIT,
/// nombres ni placas reales: documentos y NIT en el rango 99999…, nombres «FICTICI…».
/// <para>Uso de ejemplo: <c>((ConsultarConductorResponse)simulador.Responder(RpcConsultas.Conductor, Conductor("9999900011", crudo: true))).Resultado.RespuestaCruda.Json</c>.</para>
/// </summary>
public sealed class RespuestaCrudaSimuladaTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly SimuladorConsultas _simulador = new(TimeProvider.System);

    [Fact]
    public void Conductor_simulado_trae_el_crudo_de_Kyverum_con_licencia_activa_vencida_y_suspendida()
    {
        var r = (ConsultarConductorResponse)_simulador.Responder(RpcConsultas.Conductor, Conductor("9999900011", crudo: true));

        var crudo = r.Resultado.RespuestaCruda;
        crudo.Should().NotBeNull("Diagnóstico pidió la respuesta cruda");
        (crudo.Proveedor, crudo.TipoSujeto, crudo.ClaveSujeto).Should().Be(("kyverum_runt_conductor", "person", "9999900011"));
        var kyverum = JsonSerializer.Deserialize<KyverumRuntPersonaResponse>(crudo.Json, Web)!;
        kyverum.Ok.Should().BeTrue();
        kyverum.Persona!.Documento.Should().Be("9999900011", "el crudo lleva el documento consultado");
        kyverum.Licencias.Should().HaveCount(3);
        kyverum.Licencias!.Select(l => l.EstadoLicencia).Should().BeEquivalentTo(["ACTIVA", "VENCIDA", "SUSPENDIDA"]);
        kyverum.Licencias.Should().OnlyContain(l => l.DetalleLicencia!.Count > 0
            && l.DetalleLicencia.All(d => !string.IsNullOrWhiteSpace(d.Categoria) && !string.IsNullOrWhiteSpace(d.FechaVencimiento)));
        using var json = JsonDocument.Parse(crudo.Json);
        json.RootElement.TryGetProperty("identidad", out _).Should().BeTrue("la forma real de Kyverum trae el bloque identidad");
    }

    [Fact]
    public void Conductor_sin_licencias_trae_crudo_con_la_lista_vacia_y_el_error_sigue_sin_resultado()
    {
        var sinLicencias = (ConsultarConductorResponse)_simulador.Responder(RpcConsultas.Conductor, Conductor("9999900012", crudo: true));
        var invalida = (ConsultarConductorResponse)_simulador.Responder(RpcConsultas.Conductor, Conductor("9999900018", crudo: true));

        JsonSerializer.Deserialize<KyverumRuntPersonaResponse>(sinLicencias.Resultado.RespuestaCruda.Json, Web)!.Licencias.Should().BeEmpty();
        invalida.Resultado.Should().BeNull("la respuesta inválida sigue sin resultado (ni crudo)");
    }

    [Fact]
    public void Rues_simulado_trae_el_crudo_de_Verifik_con_representantes_legales_y_actividades_CIIU()
    {
        var activa = (ConsultarRuesResponse)_simulador.Responder(RpcConsultas.Rues, Rues("999990011", crudo: true));
        var cancelada = (ConsultarRuesResponse)_simulador.Responder(RpcConsultas.Rues, Rues("999990016", crudo: true));

        var crudo = activa.Resultado.RespuestaCruda;
        (crudo.Proveedor, crudo.TipoSujeto, crudo.ClaveSujeto).Should().Be(("verifik_rues", "company", "999990011"));
        var rues = JsonSerializer.Deserialize<VerifikRuesResponse>(crudo.Json, Web)!;
        rues.Data!.CommercialRegistry!.Nit.Should().Be("999990011");
        rues.Data.EconomicActivities.Should().HaveCountGreaterThanOrEqualTo(2);
        rues.Data.EconomicActivities!.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Code));
        using var json = JsonDocument.Parse(crudo.Json);
        var representantes = json.RootElement.GetProperty("data").GetProperty("commercialRegistry").GetProperty("legalRepresentatives").GetProperty("legalRepresentatives");
        representantes.GetArrayLength().Should().BeGreaterThanOrEqualTo(2, "representantes legales en la lista estructurada");
        JsonSerializer.Deserialize<VerifikRuesResponse>(cancelada.Resultado.RespuestaCruda.Json, Web)!.Data!.CommercialRegistry!.RegistrationStatus
            .Should().Be("CANCELADA", "el crudo dice lo mismo que el resultado normalizado");
    }

    [Fact]
    public void Sin_incluir_respuesta_cruda_no_hay_crudo()
    {
        ((ConsultarConductorResponse)_simulador.Responder(RpcConsultas.Conductor, Conductor("9999900011", crudo: false))).Resultado.RespuestaCruda.Should().BeNull();
        ((ConsultarRuesResponse)_simulador.Responder(RpcConsultas.Rues, Rues("999990011", crudo: false))).Resultado.RespuestaCruda.Should().BeNull();
    }

    [Fact]
    public void Los_fixtures_del_proveedor_no_traen_datos_reales()
    {
        var ensamblado = typeof(SimuladorConsultas).Assembly;
        var prefijo = $"{ensamblado.GetName().Name}.Simulacion.Fixtures.";
        var fixtures = ensamblado.GetManifestResourceNames().Where(n => n.StartsWith(prefijo + "proveedor-", StringComparison.Ordinal))
            .ToDictionary(n => n[prefijo.Length..], n =>
            {
                using var lector = new StreamReader(ensamblado.GetManifestResourceStream(n)!);
                return lector.ReadToEnd();
            });

        fixtures.Keys.Should().BeEquivalentTo("proveedor-kyverum-conductor.json", "proveedor-kyverum-conductor-sin-licencias.json", "proveedor-verifik-rues.json");
        foreach (var (archivo, json) in fixtures)
        {
            // Todo número de 6 o más dígitos es del rango ficticio 99999… (o una fecha/código CIIU corto); los nombres son FICTICI…
            Regex.Matches(json, @"\d{6,}").Select(m => m.Value).Where(n => !n.StartsWith("99999", StringComparison.Ordinal)).Should().BeEmpty(archivo);
            foreach (var nombre in Regex.Matches(json, "\"(?:name|nombreCompleto|businessName)\"\\s*:\\s*\"([^\"]*)\"").Select(m => m.Groups[1].Value)
                .Where(n => !n.StartsWith("ciiu", StringComparison.Ordinal)))
                nombre.Should().Contain("FICTICI", $"{archivo}: «{nombre}» debe ser inventado");
            json.Should().NotMatchRegex("[A-Z]{3}[0-9]{3}", $"{archivo} no trae placas");
        }
    }

    private static ConsultarConductorRequest Conductor(string numero, bool crudo) => new()
    {
        Opciones = new OpcionesConsulta { IncluirRespuestaCruda = crudo },
        Documento = new DocumentoIdentidad { Tipo = "CC", Numero = numero },
    };

    private static ConsultarRuesRequest Rues(string nit, bool crudo) => new() { Opciones = new OpcionesConsulta { IncluirRespuestaCruda = crudo }, Nit = nit };
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13724 (F00.1-H3), escenario 4 — <c>GET /api/v1/diagnostico/sesion</c>: la compañía, los roles, los permisos del
/// producto y las capacidades salen del token. Una sesión = una compañía (D-03): no hay parámetro ni cabecera que la cambie.
/// No lee la base (la fuga entre empresas de esta ruta la cubre su escenario en <c>EscenariosDeFuga</c>).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~SesionTests
/// </code>
/// </example>
public sealed class SesionTests
{
    private const string Ruta = Program.Prefijo + "/sesion";

    private static readonly string[] PermisosUsuario =
    [
        "diagnostico.diagnosticos.crear", "diagnostico.diagnosticos.ver", "diagnostico.reportes.descargar",
        "diagnostico.derivacion.crear", "diagnostico.configuracion.ver",
    ];

    // Escenario 4 — compañía, rol, permisos y capacidades del token.
    [Fact]
    public async Task Devuelve_compania_rol_permisos_y_capacidades_del_token()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var empresaA = Guid.NewGuid();
        var token = api.Token(empresa: empresaA, roles: ["usuario_diagnostico", "AdminCompany"],
            permisos: [.. PermisosUsuario, "users.read"], tipoEmpresa: "CLIENTE");

        var respuesta = await api.Cliente(token).GetAsync(Ruta, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var sesion = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        sesion.GetProperty("empresa").GetProperty("id").GetGuid().Should().Be(empresaA);
        sesion.GetProperty("empresa").GetProperty("nombre").GetString().Should().Be("Empresa de prueba");
        sesion.GetProperty("usuario").GetProperty("email").GetString().Should().Be("operador@empresa.test");
        Cadenas(sesion, "roles").Should().Contain("usuario_diagnostico");
        Cadenas(sesion, "permisos").Should().BeEquivalentTo(PermisosUsuario, "solo los permisos del producto, no los de plataforma");
        var capacidades = sesion.GetProperty("capacidades");
        capacidades.GetProperty("crearDiagnosticos").GetBoolean().Should().BeTrue();
        capacidades.GetProperty("verDiagnosticos").GetBoolean().Should().BeTrue();
        capacidades.GetProperty("derivar").GetBoolean().Should().BeTrue();
        capacidades.GetProperty("editarConfiguracion").GetBoolean().Should().BeFalse();
        capacidades.GetProperty("reprocesarConsultas").GetBoolean().Should().BeFalse();
        capacidades.GetProperty("backoffice").GetBoolean().Should().BeFalse();

        // El mismo permiso de backoffice solo da la capacidad con tenant_type=FLIT (§6.2, D-16).
        var cliente = await Sesion(api, api.Token(roles: ["backoffice_diagnostico"], permisos: ["diagnostico.backoffice.ver"], tipoEmpresa: "CLIENTE"), ct);
        var flit = await Sesion(api, api.Token(roles: ["backoffice_diagnostico"], permisos: ["diagnostico.backoffice.ver"], tipoEmpresa: "FLIT"), ct);
        cliente.GetProperty("capacidades").GetProperty("backoffice").GetBoolean().Should().BeFalse();
        flit.GetProperty("capacidades").GetProperty("backoffice").GetBoolean().Should().BeTrue();
        flit.GetProperty("empresa").GetProperty("tipo").GetString().Should().Be("FLIT");
    }

    // Escenario 4 — un parámetro o cabecera con la compañía B no cambia la sesión de A.
    [Fact]
    public async Task Ignora_parametro_de_compania_distinta()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        using var cliente = api.Cliente(api.Token(empresa: empresaA, permisos: PermisosUsuario));
        cliente.DefaultRequestHeaders.Add("X-Flit-Tenant-Id", empresaB.ToString());

        var respuesta = await cliente.GetAsync($"{Ruta}?empresa={empresaB}&tenantId={empresaB}&companiaId={empresaB}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        JsonDocument.Parse(cuerpo).RootElement.GetProperty("empresa").GetProperty("id").GetGuid().Should().Be(empresaA);
        cuerpo.Should().NotContain(empresaB.ToString(), "nada de la compañía B");
    }

    private static async Task<JsonElement> Sesion(ApiDePrueba api, string token, CancellationToken ct)
    {
        var respuesta = await api.Cliente(token).GetAsync(Ruta, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static List<string> Cadenas(JsonElement raiz, string propiedad) =>
        raiz.GetProperty(propiedad).EnumerateArray().Select(e => e.GetString()!).ToList();
}

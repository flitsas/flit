using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #14579, BE-0 (SP-2, contrato-api §0) — <c>verReportes</c> y <c>verEstadisticasGlobales</c> de <c>GET /sesion</c> salen de la misma regla
/// que autoriza las rutas: para cada perfil de la matriz SP-1, la capacidad es verdadera si y solo si una ruta protegida con
/// <see cref="ReglasEstadisticasYReportes.RequiereVerReportes{TBuilder}"/> (R1) o con
/// <see cref="ReglasEstadisticasYReportes.RequiereEstadisticasGlobales{TBuilder}"/> (E2) responde distinto de 403. Las rutas reales las
/// mapean BE-1 y BE-2 con esas mismas extensiones; aquí son sondas. Sin base: todo sale del token.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Plataforma.CapacidadesTests
/// </code>
/// </example>
public sealed class CapacidadesTests
{
    private const string SondaReportes = Program.Prefijo + "/prueba-be0/reportes";
    private const string SondaGlobales = Program.Prefijo + "/backoffice/prueba-be0/estadisticas-globales";

    /// <summary>Perfil de la matriz SP-1 con lo que se espera de cada capacidad.</summary>
    private sealed record Perfil(string Nombre, bool VerReportes, bool VerEstadisticasGlobales, Func<ApiDePrueba, string> Token);

    private static readonly Perfil[] Perfiles =
    [
        new("usuario", VerReportes: true, VerEstadisticasGlobales: false,
            a => a.Token(roles: ["usuario_diagnostico"], permisos: [Permisos.DiagnosticosVer, Permisos.ReportesVer, Permisos.ReportesDescargar], tipoEmpresa: "CLIENTE")),
        new("admin", VerReportes: true, VerEstadisticasGlobales: false,
            a => a.Token(roles: ["admin_diagnostico"], permisos: [Permisos.DiagnosticosVer, Permisos.ReportesVer, Permisos.ConfiguracionEditar], tipoEmpresa: "CLIENTE")),
        new("usuario_sin_reportes_ver", VerReportes: false, VerEstadisticasGlobales: false,
            a => a.Token(roles: ["usuario_diagnostico"], permisos: [Permisos.DiagnosticosVer, Permisos.ReportesDescargar], tipoEmpresa: "CLIENTE")),
        new("superadmin_de_otra_compania", VerReportes: true, VerEstadisticasGlobales: true,
            a => a.Token(roles: ["SuperAdmin"], permisos: [], tipoEmpresa: "CLIENTE")),
        new("flit_backoffice_y_volumetria", VerReportes: false, VerEstadisticasGlobales: true,
            a => a.Token(roles: ["operador"], permisos: OperadorBackofficeDePrueba.Slugs, tipoEmpresa: "FLIT")),
        new("flit_backoffice_sin_volumetria", VerReportes: false, VerEstadisticasGlobales: false,
            a => a.Token(roles: ["operador"], permisos: [Permisos.BackofficeVer], tipoEmpresa: "FLIT")),
        new("cliente_con_claims_de_backoffice_a_mano", VerReportes: true, VerEstadisticasGlobales: false,
            a => a.Token(roles: ["admin_diagnostico"], permisos: [Permisos.BackofficeVer, Permisos.VolumetriaVer, Permisos.ReportesVer, Permisos.ReportesDescargar], tipoEmpresa: "CLIENTE")),
    ];

    // SP-2 — la capacidad de la sesión coincide con lo que responde la ruta, perfil por perfil.
    [Fact]
    public async Task BE0_capacidades_de_la_sesion_coinciden_con_la_autorizacion_de_R1_y_E2()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync(mapear: MapearSondas);
        foreach (var perfil in Perfiles)
            await VerificarAsync(api, perfil, ct);
    }

    private static async Task VerificarAsync(ApiDePrueba api, Perfil perfil, CancellationToken ct)
    {
        using var cliente = api.Cliente(perfil.Token(api));

        var sesion = await cliente.GetAsync(Program.Prefijo + "/sesion", ct);
        sesion.StatusCode.Should().Be(HttpStatusCode.OK, await sesion.Content.ReadAsStringAsync(ct));
        var capacidades = (await sesion.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("capacidades");
        var reportes = (await cliente.GetAsync(SondaReportes, ct)).StatusCode;
        var globales = (await cliente.GetAsync(SondaGlobales, ct)).StatusCode;

        capacidades.GetProperty("verReportes").GetBoolean().Should().Be(perfil.VerReportes, perfil.Nombre);
        capacidades.GetProperty("verEstadisticasGlobales").GetBoolean().Should().Be(perfil.VerEstadisticasGlobales, perfil.Nombre);
        (reportes != HttpStatusCode.Forbidden).Should().Be(perfil.VerReportes, $"{perfil.Nombre}: R1 respondió {reportes}");
        (globales != HttpStatusCode.Forbidden).Should().Be(perfil.VerEstadisticasGlobales, $"{perfil.Nombre}: E2 respondió {globales}");
    }

    // SP-1 — un token de servicio no pasa ninguna de las dos reglas.
    [Fact]
    public async Task BE0_token_de_servicio_recibe_403_en_R1_y_E2()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync(mapear: MapearSondas);
        using var cliente = api.Cliente(api.Token(sujeto: "svc-otro", permisos: [Permisos.ReportesVer, Permisos.BackofficeVer, Permisos.VolumetriaVer], tipoEmpresa: "FLIT"));

        (await cliente.GetAsync(SondaReportes, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cliente.GetAsync(SondaGlobales, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static void MapearSondas(WebApplication app)
    {
        app.MapGet(SondaReportes, () => Results.Ok()).RequireAuthorization().RequiereVerReportes();
        app.MapGet(SondaGlobales, () => Results.Ok()).RequireAuthorization().RequiereEstadisticasGlobales();
    }
}

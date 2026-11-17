using System.Net;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #13728 (F00.1-H5), escenarios 1 y 2 — <c>PoliticaBackoffice</c> exige a la vez el permiso
/// <c>diagnostico.backoffice.ver</c> y el claim <c>tenant_type=FLIT</c> (D-16). Con el rol de backoffice en una compañía que
/// no es FLIT, o en la compañía FLIT sin el permiso, toda ruta <c>/backoffice/*</c> responde 403.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~PoliticaBackofficeTests
/// </code>
/// </example>
public sealed class PoliticaBackofficeTests
{
    private const string Resumen = Program.Prefijo + BackofficeEndpoints.Grupo + BackofficeEndpoints.RutaResumen;

    private static readonly string[] RolBackoffice = ["backoffice_diagnostico"];

    // Escenario 1 — backoffice_diagnostico en una compañía con tenant_type distinto de FLIT (o sin el claim): 403.
    [Fact]
    public async Task Backoffice_en_compania_no_FLIT_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var url = $"{Resumen}?empresa={Guid.NewGuid()}";

        foreach (var tipo in new[] { "CLIENTE", "RENTING", "CONCESIONARIO", null })
        {
            var token = api.Token(permisos: [Permisos.BackofficeVer], roles: RolBackoffice, tipoEmpresa: tipo);
            var respuesta = await api.Cliente(token).GetAsync(url, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"tenant_type={tipo ?? "(sin claim)"} no es FLIT");
            (await api.Cliente(token).GetAsync(Program.Prefijo + BackofficeEndpoints.Grupo + "/diagnosticos?empresa=" + Guid.NewGuid(), ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, "cualquier ruta /backoffice/*, también una que aún no existe");
        }

        var superAdminNoFlit = api.Token(permisos: [Permisos.BackofficeVer], roles: ["SuperAdmin"], tipoEmpresa: "CLIENTE");
        (await api.Cliente(superAdminNoFlit).GetAsync(url, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "ni el SuperAdmin abre el backoffice fuera de la compañía FLIT");
        (await api.Anonimo().GetAsync(url, ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "sin token sigue siendo 401");
    }

    // Escenario 2 — compañía FLIT sin diagnostico.backoffice.ver (aunque tenga otros permisos, también de backoffice): 403.
    [Fact]
    public async Task Tenant_FLIT_sin_permiso_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var token = api.Token(
            permisos: [Permisos.DiagnosticosVer, Permisos.ConfiguracionContratoEditar, Permisos.AuditoriaVer, Permisos.VolumetriaVer],
            roles: RolBackoffice, tipoEmpresa: "FLIT");

        var respuesta = await api.Cliente(token).GetAsync($"{Resumen}?empresa={Guid.NewGuid()}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, "el permiso de backoffice es obligatorio además de tenant_type=FLIT");
    }

    // Escenarios 1 y 2, caso positivo — permiso y tenant_type=FLIT a la vez: 200.
    [Fact]
    public async Task Permiso_y_tenant_FLIT_responde_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var token = api.Token(permisos: [Permisos.BackofficeVer], roles: RolBackoffice, tipoEmpresa: "FLIT");

        var respuesta = await api.Cliente(token).GetAsync($"{Resumen}?empresa={Guid.NewGuid()}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
    }
}

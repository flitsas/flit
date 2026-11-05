using System.Net;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #13726 (F00.1-H4), escenarios 4 y 5 — un rol de otro producto no abre Diagnóstico: las rutas de negocio solo
/// aceptan permisos <c>diagnostico.*</c> del manifiesto (<see cref="AutorizacionExtensions.RequirePermiso"/> no admite otro
/// slug y <c>GET /sesion</c> exige al menos un permiso del producto). El <c>admin_diagnostico</c> que la plataforma
/// asigna a todo AdminCompany no da acceso con el producto apagado (R-12).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~RolesDeOtroProductoTests
/// </code>
/// </example>
public sealed class RolesDeOtroProductoTests
{
    private const string Sesion = Program.Prefijo + "/sesion";
    private const string RutaConPermiso = Program.Prefijo + "/__prueba-roles/diagnosticos";

    /// <summary>Permisos de Trámites como los trae el token de un usuario de ese producto.</summary>
    private static readonly string[] PermisosDeTramites = ["procedures.read", "procedures.create", "users.read"];

    private static readonly string[] PermisosAdminDiagnostico =
        Permisos.RolesPorDefecto.Single(r => r.Codigo == "admin_diagnostico").Permisos;

    // Escenario 4 — rol de Trámites sin rol de Diagnóstico, compañía con Diagnóstico activo: 403.
    [Fact]
    public async Task Rol_de_tramites_sin_rol_de_diagnostico_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync(MapearRutaConPermiso);
        var empresa = Guid.NewGuid();
        api.Productos.Encender(empresa);
        using var tramites = api.Cliente(api.Token(empresa: empresa, permisos: PermisosDeTramites, roles: ["AdminCompany", "gestor_tramites"]));

        (await tramites.GetAsync(Sesion, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sin un permiso de Diagnóstico no hay sesión del producto");
        (await tramites.GetAsync(RutaConPermiso, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "el permiso es diagnostico.*, no uno de Trámites");

        using var diagnostico = api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]));
        (await diagnostico.GetAsync(RutaConPermiso, ct)).StatusCode.Should().Be(HttpStatusCode.OK, "con el permiso del producto sí entra");
    }

    // Escenario 4 — una ruta no puede protegerse con el permiso de otro producto: RequirePermiso solo acepta el manifiesto.
    [Theory]
    [InlineData("procedures.read")]
    [InlineData("tramites.diagnosticos.ver")]
    [InlineData("diagnostico.inexistente.ver")]
    public async Task RequirePermiso_solo_acepta_slugs_del_manifiesto_de_diagnostico(string slug)
    {
        var mapear = async () =>
        {
            await using var api = await ApiDePrueba.SinBaseAsync(app => app.MapGet(Program.Prefijo + "/__prueba-roles/otro", () => Results.Ok()).RequirePermiso(slug));
        };

        await mapear.Should().ThrowAsync<ArgumentException>().WithMessage($"*{slug}*");
    }

    // Escenario 5 — AdminCompany con admin_diagnostico (y todos sus permisos) en una compañía con Diagnóstico apagado: 403.
    [Fact]
    public async Task Admin_diagnostico_con_producto_apagado_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync(MapearRutaConPermiso);
        var empresa = Guid.NewGuid();
        api.Productos.Apagar(empresa);
        using var admin = api.Cliente(api.Token(empresa: empresa, permisos: [.. PermisosAdminDiagnostico, "users.read", "roles.manage"],
            roles: ["AdminCompany", "admin_diagnostico"]));

        (await admin.GetAsync(Sesion, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "el admin auto-asignado no abre un producto apagado");
        (await admin.GetAsync(RutaConPermiso, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        api.Productos.Consultas(empresa).Should().Be(2, "la negación viene del guard de suscripción, no de los permisos");
    }

    private static void MapearRutaConPermiso(WebApplication app) =>
        app.MapGet(RutaConPermiso, () => Results.Ok()).RequirePermiso(Permisos.DiagnosticosVer);
}

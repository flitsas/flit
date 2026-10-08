using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Api.Endpoints;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Application;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #13442 — solo SuperAdmin y Admin de Compañía gestionan roles; ot_admin y los demás roles reciben 403 en toda
/// creación, edición o eliminación, pero conservan la lectura del listado y la asignación de roles a usuarios.
/// Se verifica el mapa real de endpoints (política exigida por ruta) y el handler de la política, sin levantar la API.
/// </summary>
public sealed class RoleManagementAuthorizationTests
{
    private static IReadOnlyList<RouteEndpoint> Endpoints()
    {
        var builder = WebApplication.CreateBuilder();
        // Solo para que la inferencia de parámetros de los minimal APIs reconozca los servicios; nada se resuelve.
        builder.Services.AddSecurityApplication();
        builder.Services.AddDbContext<FlitDbContext>(o => o.UseInMemoryDatabase(nameof(RoleManagementAuthorizationTests)));
        builder.Services.AddScoped<Flit.Modules.Security.Domain.Roles.IRoleRepository, Flit.Infrastructure.Persistence.Repositories.RoleRepository>();
        var app = builder.Build();
        app.MapSecurityEndpoints();
        return [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()];
    }

    private static string[] Policies(RouteEndpoint e) =>
        [.. e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy ?? "(authenticated)")];

    private static bool IsRoleWrite(RouteEndpoint e) =>
        e.RoutePattern.RawText!.StartsWith("/api/v1/security/roles", StringComparison.Ordinal)
        && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Any(m => m is "POST" or "PUT" or "PATCH" or "DELETE");

    // AC1
    [Fact]
    public void TodaEscrituraDeRolesDelTenantExigeLaPoliticaAdminCompany()
    {
        var escrituras = Endpoints().Where(IsRoleWrite).ToList();

        escrituras.Should().HaveCount(4, "crear, editar, cambiar permisos y eliminar");
        foreach (var e in escrituras)
            Policies(e).Should().Contain(AdminAuthorization.AdminCompanyPolicy, e.RoutePattern.RawText);
    }

    [Fact]
    public void LaLecturaDeDetalleYPermisosOtorgablesTambienExigeAdminCompany()
    {
        var lecturas = Endpoints().Where(e =>
            e.RoutePattern.RawText!.StartsWith("/api/v1/security/roles/", StringComparison.Ordinal)
            && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET")).ToList();

        lecturas.Should().HaveCount(2);
        foreach (var e in lecturas)
            Policies(e).Should().Contain(AdminAuthorization.AdminCompanyPolicy, e.RoutePattern.RawText);
    }

    // AC2 — se conserva: el listado para asignar sigue disponible a cualquier usuario autenticado (no exige AdminCompany)
    [Fact]
    public void ElListadoDeRolesParaAsignarNoExigeAdminCompany()
    {
        var listado = Endpoints().Single(e =>
            e.RoutePattern.RawText == "/api/v1/security/roles"
            && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));

        Policies(listado).Should().NotContain(AdminAuthorization.AdminCompanyPolicy);
    }

    // AC2 — asignar/quitar rol a un usuario sigue sin exigir AdminCompany (el alcance por tenant lo impone el handler)
    [Fact]
    public void AsignarYQuitarRolAUsuariosNoCambiaDePolitica()
    {
        var rutas = Endpoints().Where(e =>
            e.RoutePattern.RawText!.Contains("/users/{userId:guid}/role", StringComparison.Ordinal)).ToList();

        rutas.Should().NotBeEmpty();
        foreach (var e in rutas)
            Policies(e).Should().NotContain(AdminAuthorization.AdminCompanyPolicy, e.RoutePattern.RawText);
    }

    // AC1 — ot_admin y roles personalizados no satisfacen la política; AdminCompany y SuperAdmin sí
    [Theory]
    [InlineData("ot_admin", false)]
    [InlineData("gestor_tramites_ot", false)]
    [InlineData("contador", false)]
    [InlineData("AdminCompany", true)]
    [InlineData("SuperAdmin", true)]
    public async Task LaPoliticaAdminCompanySoloLaCumplenAdminCompanyYSuperAdmin(string role, bool esperado)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AdminAuthorization.RoleClaimType, role), new Claim("permissions", "roles.manage")], "test"));
        var requirement = new AdminCompanyRequirement();
        var ctx = new AuthorizationHandlerContext([requirement], user, null);

        await new AdminCompanyAuthorizationHandler().HandleAsync(ctx);

        ctx.HasSucceeded.Should().Be(esperado);
    }
}

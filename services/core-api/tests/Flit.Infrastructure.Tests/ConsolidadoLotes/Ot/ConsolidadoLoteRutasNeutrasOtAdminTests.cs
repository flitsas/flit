using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Tramites;
using Flit.Api.Middleware;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Flit.Infrastructure.Tests.ConsolidadoLotes.Ot;

/// <summary>
/// HU #13392 (arrastre de #13391, D-FB5) — las rutas neutras del motor <c>/api/v1/consolidados/lotes/*</c> (consulta,
/// descarga y cancelación, dueño = <c>sub</c>) deben aceptar al <c>ot_admin</c>, cuyo tenant del token es el del OT y no
/// una compañía. En esta rama esas rutas aún no están publicadas (#13379 y #13307): se cubre lo que ya existe y que
/// decide si pasaría — el middleware de tenant no las intercepta (no exige compañía) y el requisito de permiso
/// <c>consolidado-masivo.download</c> admite el token del <c>ot_admin</c>.
/// <para>Uso de ejemplo:
/// <c>TenantEnforcementMiddleware.IsRuntimeScoped("/api/v1/consolidados/lotes/actual")</c> ⇒ <c>false</c>.</para>
/// </summary>
public sealed class ConsolidadoLoteRutasNeutrasOtAdminTests
{
    private static readonly Guid TenantOt = Guid.Parse("e1339200-0000-4000-8000-0000000000a1");

    public static TheoryData<string> RutasNeutras() =>
    [
        ConsolidadoLoteEndpoints.RutaLotes,
        $"{ConsolidadoLoteEndpoints.RutaLotes}/actual",
        $"{ConsolidadoLoteEndpoints.RutaLotes}/{Guid.NewGuid()}",
        $"{ConsolidadoLoteEndpoints.RutaLotes}/{Guid.NewGuid()}/partes/1",
        $"{ConsolidadoLoteEndpoints.RutaLotes}/{Guid.NewGuid()}/cancelar",
    ];

    private static ClaimsPrincipal OtAdmin(bool conPermiso = true)
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new(AdminAuthorization.TenantIdClaimType, TenantOt.ToString()),
            new("role_code", "ot_admin"),
        };
        if (conPermiso)
            claims.Add(new Claim("permissions", ConsolidadoLotePermisos.Descargar));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    [Theory]
    [MemberData(nameof(RutasNeutras))]
    public async Task ArrastreFB4_RutaNeutra_ElMiddlewareDeTenantDejaPasarAlOtAdmin_SinExigirCompania(string ruta)
    {
        TenantEnforcementMiddleware.IsRuntimeScoped(new PathString(ruta)).Should().BeFalse(
            "el dueño del lote es el sub; el tenant OT del token no es una compañía de trámites");

        var siguiente = false;
        var http = new DefaultHttpContext { User = OtAdmin() };
        http.Request.Path = ruta;

        await new TenantEnforcementMiddleware(_ =>
        {
            siguiente = true;
            return Task.CompletedTask;
        }).InvokeAsync(http);

        siguiente.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK, "sin 401/403 del middleware");
    }

    [Fact]
    public async Task ArrastreFB4_ElPermisoDeLasRutasNeutras_AdmiteElTokenDelOtAdmin_YSinElPermisoNo()
    {
        var handler = new PermissionAuthorizationHandler();

        var conPermiso = new AuthorizationHandlerContext(
            [new PermissionRequirement(ConsolidadoLotePermisos.Descargar)], OtAdmin(), resource: null);
        await handler.HandleAsync(conPermiso);

        var sinPermiso = new AuthorizationHandlerContext(
            [new PermissionRequirement(ConsolidadoLotePermisos.Descargar)], OtAdmin(conPermiso: false), resource: null);
        await handler.HandleAsync(sinPermiso);

        conPermiso.HasSucceeded.Should().BeTrue("el grant directo a ot_admin (#13369) llega en el claim permissions");
        sinPermiso.HasSucceeded.Should().BeFalse();
    }
}

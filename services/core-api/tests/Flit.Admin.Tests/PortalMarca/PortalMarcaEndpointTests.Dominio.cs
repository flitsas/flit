using System.Net;
using FluentAssertions;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>
/// HU #14381 AC5 — el alcance de <c>/api/v1/admin/ot/portal-dominio</c> por HTTP: un <c>ot_admin</c> que nombra a otro organismo
/// recibe 403 en todas las rutas, y un rol ajeno no entra.
/// </summary>
public sealed partial class PortalMarcaEndpointTests
{
    private const string RutaDominio = "/api/v1/admin/ot/portal-dominio";

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "")]
    [InlineData("POST", "/verificar")]
    [InlineData("DELETE", "")]
    public async Task Dominio_OtAdminQueNombraAOtroOrganismo_Recibe403(string metodo, string sub)
    {
        using var peticion = new HttpRequestMessage(new HttpMethod(metodo), $"{RutaDominio}{sub}?transitOfficeId={OrganismoB}");
        if (metodo == "POST" && sub.Length == 0)
            peticion.Content = Json(new { host = "portal.ejemplo.test" });

        var r = await Cliente(OtAdmin(TenantA)).SendAsync(peticion, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dominio_SinSesion_Responde401()
    {
        var r = await _factory.CreateClient().GetAsync(RutaDominio, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;


namespace Flit.Admin.Tests.Authorization;

/// <summary>HU #13136 (Feature #13115) — reactivar restaurando vínculos sin desplazar el default, sobre PostgreSQL real.</summary>
public sealed class MandateSignerReactivateEndpointsTests(WebApplicationFactory<Program> factory)
    : MandateSignerEndpointsTestBase(factory)
{
    [Fact]
    public async Task HU13136_reactivar_restaura_vinculos_y_default_vacio_y_responde_el_detalle()
    {
        var signer = await SeedSignerAsync("Ana", [(_companyA, "organismo"), (_companyB, "organismo")]);
        await SeedDefaultsAsync(signer);
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías
        (await _client.PostAsync(Hub(signer, "/inactivate"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.PostAsync(Hub(signer, "/reactivate"), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("restoredLinks").GetArrayLength().Should().Be(2);
        body.GetProperty("conflictLinks").GetArrayLength().Should().Be(0);
        body.GetProperty("restoredDefaults").GetInt32().Should().Be(2);

        await using var db = NewDb();
        (await db.MandateSignerCompanies.AsNoTracking().Where(c => c.MandateSignerId == signer).ToListAsync(Ct))
            .Should().OnlyContain(c => c.IsActive);
        (await db.CompanyOtMandateRules.AsNoTracking().SingleAsync(r => r.TransitOfficeId == _officeA, Ct))
            .DefaultMandateSignerId.Should().Be(signer);
    }

    [Fact]
    public async Task HU13136_AC4_el_conflicto_total_responde_409_mandatario_activo_existente()
    {
        var signer = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías
        (await _client.PostAsync(Hub(signer, "/inactivate"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await SeedSignerAsync("Beto", [(_companyA, "organismo")]);

        var response = await _client.PostAsync(Hub(signer, "/reactivate"), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .Should().Be("mandatario_activo_existente");
        await using var db = NewDb();
        (await db.MandateSigners.AsNoTracking().SingleAsync(s => s.Id == signer, Ct)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task HU13136_AC5_reactivar_un_eliminado_es_404()
    {
        var signer = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías
        (await _client.DeleteAsync(Hub(signer, "?confirmarImpacto=true"), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.PostAsync(Hub(signer, "/reactivate"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

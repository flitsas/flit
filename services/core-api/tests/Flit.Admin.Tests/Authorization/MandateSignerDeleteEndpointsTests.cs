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

/// <summary>
/// HU #13135 (Feature #13115) — eliminar (baja lógica), impacto y confirmación sobre PostgreSQL real, con el candado
/// por origen y rol aplicado también a DELETE e impacto.
/// </summary>
public sealed class MandateSignerDeleteEndpointsTests(WebApplicationFactory<Program> factory)
    : MandateSignerEndpointsTestBase(factory)
{
    // ── Permisos por origen y rol (HU #13134) sobre las rutas nuevas ─────────────────────────────

    [Theory]
    [InlineData("DELETE")]
    [InlineData("impact")]
    public async Task El_Admin_de_Compania_no_elimina_ni_consulta_el_impacto_de_un_mandatario_del_organismo(string operacion)
    {
        var organismo = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        AuthenticateCompanyA();

        var response = operacion == "DELETE"
            ? await _client.DeleteAsync(Company(_companyA, organismo, "?confirmarImpacto=true"), Ct)
            : await _client.GetAsync(Company(_companyA, organismo, "/impact"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .Should().Be("mandatario_configurado_por_organismo");
        await AssertSignerUntouchedAsync(organismo);
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("impact")]
    public async Task Usuario_OT_sin_ot_admin_recibe_403_en_eliminar_e_impacto(string operacion)
    {
        var organismo = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        AuthenticateOt("gestor_tramites_ot");

        var response = operacion == "DELETE"
            ? await _client.DeleteAsync(Hub(organismo, "?confirmarImpacto=true"), Ct)
            : await _client.GetAsync(Hub(organismo, "/impact"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertSignerUntouchedAsync(organismo);
    }

    [Fact]
    public async Task El_Gestor_recibe_403_al_eliminar_por_la_ruta_de_compania()
    {
        var cliente = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        AuthenticateGestor();

        (await _client.DeleteAsync(Company(_companyA, cliente, "?confirmarImpacto=true"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertSignerUntouchedAsync(cliente);
    }

    [Fact]
    public async Task El_Admin_de_otra_compania_recibe_403_al_eliminar_sin_revelar_si_existe()
    {
        var existente = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        AuthenticateCompanyB();

        var conExistente = await _client.DeleteAsync(Company(_companyA, existente, "?confirmarImpacto=true"), Ct);
        var inexistente = await _client.DeleteAsync(Company(_companyA, Guid.NewGuid(), "?confirmarImpacto=true"), Ct);

        conExistente.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        inexistente.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await conExistente.Content.ReadAsStringAsync(Ct)).Should().Be(await inexistente.Content.ReadAsStringAsync(Ct));
        await AssertSignerUntouchedAsync(existente);
    }

    [Fact]
    public async Task El_Super_Admin_elimina_cualquier_mandatario_por_las_rutas_de_compania_y_del_hub()
    {
        var organismo = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        var cliente = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        AuthenticateSuperAdmin();

        (await _client.DeleteAsync(Company(_companyA, organismo, "?confirmarImpacto=true"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync(Hub(cliente, "?confirmarImpacto=true"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task El_Admin_OT_elimina_mandatarios_de_cualquier_origen_y_el_Admin_de_Compania_solo_los_suyos()
    {
        var organismo = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        var cliente = await SeedSignerAsync("Beto", [(_companyA, "compania")]);

        AuthenticateOt();
        (await _client.DeleteAsync(Hub(organismo, "?confirmarImpacto=true"), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        AuthenticateCompanyA();
        (await _client.DeleteAsync(Company(_companyA, cliente, "?confirmarImpacto=true"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task La_cabeza_de_red_elimina_lo_de_origen_compania_de_una_hija_y_no_lo_de_origen_organismo()
    {
        var delCliente = await SeedSignerAsync("Hija cliente", [(_child, "compania")]);
        var delOrganismo = await SeedSignerAsync("Hija organismo", [(_child, "organismo")]);
        AuthenticateHead();

        (await _client.DeleteAsync(Child(delCliente, "?confirmarImpacto=true"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync(Child(delOrganismo, "?confirmarImpacto=true"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertSignerUntouchedAsync(delOrganismo);
    }

    [Fact]
    public async Task HU13135_baja_con_impacto_sin_confirmar_es_409_y_con_confirmacion_es_204_y_retira_los_defaults()
    {
        var signer = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        await SeedDefaultsAsync(signer);
        AuthenticateOt();

        var impacto = await _client.GetFromJsonAsync<JsonElement>(Hub(signer, "/impact"), Ct);
        var data = impacto.GetProperty("data");
        data.GetProperty("hasImpact").GetBoolean().Should().BeTrue();
        data.GetProperty("onlyActiveFor").GetArrayLength().Should().Be(1);
        data.GetProperty("defaults").GetArrayLength().Should().Be(2);

        var sinConfirmar = await _client.DeleteAsync(Hub(signer), Ct);
        sinConfirmar.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await sinConfirmar.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .Should().Be("mandatario_baja_requiere_confirmacion");
        await AssertSignerUntouchedAsync(signer);

        var confirmado = await _client.DeleteAsync(Hub(signer, "?confirmarImpacto=true"), Ct);
        confirmado.StatusCode.Should().Be(HttpStatusCode.NoContent);
        confirmado.Headers.GetValues("X-Mandatario-Reasignados").Single().Should().Be("0");

        await using var db = NewDb();
        (await db.MandateSigners.AsNoTracking().SingleAsync(s => s.Id == signer, Ct)).DeletedAt.Should().NotBeNull();
        (await db.CompanyOtMandateRules.AsNoTracking().SingleAsync(r => r.TransitOfficeId == _officeA, Ct))
            .DefaultMandateSignerId.Should().BeNull();
        (await db.TransitOfficeMandateConfigs.AsNoTracking().SingleAsync(c => c.TransitOfficeId == _officeA, Ct))
            .DefaultMandateSignerId.Should().BeNull();

        // Doble eliminación: 404 y nada nuevo en la bitácora.
        var eventos = await db.TenantConfigAuditLogs.CountAsync(l => l.TargetEntityId == signer, Ct);
        (await _client.DeleteAsync(Hub(signer, "?confirmarImpacto=true"), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await db.TenantConfigAuditLogs.CountAsync(l => l.TargetEntityId == signer, Ct)).Should().Be(eventos);

        var bitacora = await db.TenantConfigAuditLogs.AsNoTracking()
            .Where(l => l.TargetEntityId == signer && l.Module == "mandatarios").ToListAsync(Ct);
        bitacora.Should().ContainSingle(l => l.FieldName == "deleted" && l.Operation == "delete" && l.ChangedBy == _otAdminUser);
    }

    [Fact]
    public async Task HU13135_baja_sin_impacto_responde_204_sin_confirmar()
    {
        var signer = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        AuthenticateOt();

        (await _client.DeleteAsync(Hub(signer), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}

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
/// HU #13134 (Feature #13115) — permisos por origen y rol y candado «Configurado por el organismo de tránsito»,
/// sobre PostgreSQL real y los endpoints reales. Matriz: rol (Super Admin, Admin OT, Admin de Compañía
/// propio/ajeno/cabeza de red, Gestor, usuario OT sin <c>ot_admin</c>) × origen del mandatario (organismo de tránsito
/// / compañía) × ruta (alta, edición, inactivar, reactivar).
/// </summary>
public sealed class MandateSignerOriginPermissionsTests(WebApplicationFactory<Program> factory)
    : MandateSignerEndpointsTestBase(factory)
{
    // ── Escenario: Admin de Compañía frente a un mandatario configurado por el organismo ─────────

    public static TheoryData<string> EscriturasDeCompania => new()
    {
        { "PUT" }, { "inactivate" }, { "reactivate" },
    };

    [Theory]
    [MemberData(nameof(EscriturasDeCompania))]
    public async Task AC2_Admin_de_Compania_no_toca_un_mandatario_configurado_por_el_organismo(string operacion)
    {
        var organismo = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        AuthenticateCompanyA();

        var response = await SendCompanyAsync(operacion, Company(_companyA, organismo));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("code").GetString().Should().Be("mandatario_configurado_por_organismo");
        await AssertSignerUntouchedAsync(organismo);
    }

    [Fact]
    public async Task AC3_Admin_de_Compania_gestiona_un_mandatario_configurado_por_el_cliente()
    {
        var cliente = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        AuthenticateCompanyA();

        // Edita: el candado no lo frena (el cuerpo vacío lo frena la validación, no el permiso).
        var put = await _client.PutAsJsonAsync(Company(_companyA, cliente), new { }, Ct);
        put.StatusCode.Should().NotBe(HttpStatusCode.Forbidden).And.NotBe(HttpStatusCode.NotFound);

        (await _client.PostAsync(Company(_companyA, cliente, "/inactivate"), null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostAsync(Company(_companyA, cliente, "/reactivate"), null, Ct))
            .IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task AC4_Super_Admin_gestiona_cualquier_mandatario_por_las_rutas_de_compania_y_del_hub()
    {
        var organismo = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        var cliente = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        AuthenticateSuperAdmin();

        (await _client.PostAsync(Company(_companyA, organismo, "/inactivate"), null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostAsync(Hub(cliente, "/inactivate"), null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AC1_Admin_OT_gestiona_sus_mandatarios_y_no_los_de_compania_en_su_organismo()
    {
        var delOt = await SeedSignerAsync("Ana", []);
        var deCompania = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        AuthenticateOt();

        (await _client.PostAsync(Hub(delOt, "/inactivate"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostAsync(Hub(delOt, "/reactivate"), null, Ct)).IsSuccessStatusCode.Should().BeTrue();
        var put = await _client.PutAsJsonAsync(Hub(delOt), new { }, Ct);
        put.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);

        var denegado = await _client.PostAsync(Hub(deCompania, "/inactivate"), null, Ct);
        denegado.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denegado.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .Should().Be("mandatario_de_compania");
    }

    private string HubBase => $"/api/v1/admin/transit-offices/{_officeA}/mandate-signers";

    private object AltaCon(params Guid[] companias) => new
    {
        fullName = "Ana Restrepo",
        documentNumber = "1020304050",
        documentType = "CC",
        companyTenantIds = companias,
        transitOfficeIds = new[] { _officeA },
        signerModel = "juridica",
    };

    [Fact]
    public async Task Admin_OT_no_da_de_alta_un_mandatario_con_companias_403_y_sin_companias_201()
    {
        AuthenticateOt();

        var conCompanias = await _client.PostAsJsonAsync(HubBase, AltaCon(_companyA), Ct);
        conCompanias.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await conCompanias.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .Should().Be("mandatario_de_compania");

        var sinCompanias = await _client.PostAsJsonAsync(HubBase, AltaCon(), Ct);
        sinCompanias.StatusCode.Should().Be(HttpStatusCode.Created, await sinCompanias.Content.ReadAsStringAsync(Ct));
        var creado = (await sinCompanias.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        _signerIds.Add(creado);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("inactivate")]
    [InlineData("DELETE")]
    [InlineData("resend")]
    public async Task Admin_OT_recibe_403_al_operar_un_mandatario_de_compania_y_este_no_cambia(string operacion)
    {
        var deCompania = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        AuthenticateOt();

        var response = operacion switch
        {
            "PUT" => await _client.PutAsJsonAsync(Hub(deCompania), new { }, Ct),
            "inactivate" => await _client.PostAsync(Hub(deCompania, "/inactivate"), null, Ct),
            "DELETE" => await _client.DeleteAsync(Hub(deCompania, "?confirmarImpacto=true"), Ct),
            _ => await _client.PostAsync(Hub(deCompania, "/identity-validation/resend"), null, Ct),
        };

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .Should().Be("mandatario_de_compania");
        await AssertSignerUntouchedAsync(deCompania);
    }

    [Fact]
    public async Task Super_Admin_si_edita_e_inactiva_un_mandatario_de_compania_por_la_ruta_del_hub()
    {
        var deCompania = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        AuthenticateSuperAdmin();

        var put = await _client.PutAsJsonAsync(Hub(deCompania), new { }, Ct);
        put.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        (await _client.PostAsync(Hub(deCompania, "/inactivate"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Escenario: usuario OT sin ot_admin y Gestor ─────────────────────────────────────────────

    public static TheoryData<string> EscriturasDelHub => new()
    {
        { "POST" }, { "PUT" }, { "inactivate" }, { "reactivate" },
    };

    [Theory]
    [MemberData(nameof(EscriturasDelHub))]
    public async Task AC5_Usuario_OT_sin_ot_admin_recibe_403_en_toda_escritura_del_hub(string operacion)
    {
        var organismo = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        AuthenticateOt("gestor_tramites_ot");

        var response = operacion switch
        {
            "POST" => await _client.PostAsJsonAsync($"/api/v1/admin/transit-offices/{_officeA}/mandate-signers", new { }, Ct),
            "PUT" => await _client.PutAsJsonAsync(Hub(organismo), new { }, Ct),
            "inactivate" => await _client.PostAsync(Hub(organismo, "/inactivate"), null, Ct),
            _ => await _client.PostAsync(Hub(organismo, "/reactivate"), null, Ct),
        };

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertSignerUntouchedAsync(organismo);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("inactivate")]
    public async Task AC6_Gestor_recibe_403_al_crear_editar_o_eliminar_por_cualquier_ruta(string operacion)
    {
        var cliente = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        AuthenticateGestor();

        var response = operacion switch
        {
            "POST" => await _client.PostAsJsonAsync($"/api/v1/admin/companies/{_companyA}/mandate-signers", new { }, Ct),
            "PUT" => await _client.PutAsJsonAsync(Company(_companyA, cliente), new { }, Ct),
            _ => await _client.PostAsync(Company(_companyA, cliente, "/inactivate"), null, Ct),
        };

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertSignerUntouchedAsync(cliente);
    }

    // ── Escenario: fuera de alcance ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC7_Admin_de_otra_compania_recibe_403_y_no_se_revela_si_el_mandatario_existe()
    {
        var existente = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        AuthenticateCompanyB();

        var conExistente = await _client.PostAsync(Company(_companyA, existente, "/inactivate"), null, Ct);
        var inexistente = await _client.PostAsync(Company(_companyA, Guid.NewGuid(), "/inactivate"), null, Ct);

        conExistente.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        inexistente.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await conExistente.Content.ReadAsStringAsync(Ct)).Should().Be(await inexistente.Content.ReadAsStringAsync(Ct));
        await AssertSignerUntouchedAsync(existente);
    }

    [Fact]
    public async Task AC7_Admin_OT_de_otro_organismo_recibe_403()
    {
        var organismo = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        AuthenticateOt();

        var response = await _client.PostAsync(
            $"/api/v1/admin/transit-offices/{_officeB}/mandate-signers/{organismo}/inactivate", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertSignerUntouchedAsync(organismo);
    }

    // ── Borde: cabeza de red ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC8_Cabeza_de_red_edita_lo_de_origen_compania_de_una_hija_y_no_lo_de_origen_organismo()
    {
        var delCliente = await SeedSignerAsync("Hija cliente", [(_child, "compania")]);
        var delOrganismo = await SeedSignerAsync("Hija organismo", [(_child, "organismo")]);
        AuthenticateHead();

        (await _client.PostAsync(Child(delCliente, "/inactivate"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var bloqueado = await _client.PostAsync(Child(delOrganismo, "/inactivate"), null, Ct);
        bloqueado.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await bloqueado.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .Should().Be("mandatario_configurado_por_organismo");
        (await _client.PutAsJsonAsync(Child(delOrganismo), new { }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertSignerUntouchedAsync(delOrganismo);
    }

    // ── Banderas por rol en el listado ──────────────────────────────────────────────────────────

    [Fact]
    public async Task El_listado_de_la_compania_trae_origen_y_banderas_segun_el_rol()
    {
        var organismo = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        var cliente = await SeedSignerAsync("Beto", [(_companyA, "compania")]);

        AuthenticateCompanyA();
        var comoCompania = await ListAsync($"/api/v1/admin/companies/{_companyA}/mandate-signers");
        Flags(comoCompania, organismo).Should().Be(("organismo", false, false));
        Flags(comoCompania, cliente).Should().Be(("compania", true, true));

        AuthenticateSuperAdmin();
        var comoSuper = await ListAsync($"/api/v1/admin/companies/{_companyA}/mandate-signers");
        Flags(comoSuper, organismo).Should().Be(("organismo", true, true));
    }

    [Fact]
    public async Task El_listado_del_hub_trae_banderas_segun_el_rol()
    {
        var organismo = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        await SeedGrantAsync(_companyA);   // Bug #12912: el organismo solo ve compañías con grant o trámites entregados.
        var url = $"/api/v1/admin/transit-offices/{_officeA}/mandate-signers";

        // Regla del OT: gestiona solo lo suyo (sin compañías); el de compañía llega sin permiso de editar ni eliminar.
        AuthenticateOt();
        var lista = await ListAsync(url);
        Flags(lista, organismo).Should().Be(("organismo", false, false));
        var delOt = await SeedSignerAsync("Del OT", []);
        var propio = Flags(await ListAsync(url), delOt);
        propio.Edit.Should().BeTrue();
        propio.Delete.Should().BeTrue();
        AuthenticateSuperAdmin();
        Flags(await ListAsync(url), organismo).Should().Be(("organismo", true, true));

        AuthenticateOt("gestor_tramites_ot");
        Flags(await ListAsync(url), organismo).Should().Be(("organismo", false, false));
    }

    private async Task<HttpResponseMessage> SendCompanyAsync(string operacion, string baseUrl) => operacion switch
    {
        "PUT" => await _client.PutAsJsonAsync(baseUrl, new { }, Ct),
        "inactivate" => await _client.PostAsync(baseUrl + "/inactivate", null, Ct),
        _ => await _client.PostAsync(baseUrl + "/reactivate", null, Ct),
    };
}

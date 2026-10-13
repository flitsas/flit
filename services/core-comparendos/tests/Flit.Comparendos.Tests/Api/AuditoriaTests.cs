using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>RN-010 — historial de cambios visible para la gestora (lo suyo) y para el SuperAdmin (todo y lo global).</summary>
public sealed class AuditoriaTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    [Fact]
    public async Task Historial_DeLaEmpresa_YGlobal()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api);
        var admin = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoGestionar, Permisos.ConfiguracionVer]));
        (await admin.PostAsJsonAsync("/api/v1/comparendos/monitoreo/nits", new { numero = "900555444", nombreTitular = "AUDITADA S.A.S." }, ct)).EnsureSuccessStatusCode();

        var propio = await admin.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/auditoria", ct);
        propio.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
        propio.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("tenantId").GetGuid()).Should().OnlyContain(t => t == empresa);
        (await admin.GetAsync($"/api/v1/comparendos/auditoria?empresa={Guid.NewGuid()}", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/v1/comparendos/admin/auditoria", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.PutAsJsonAsync("/api/v1/comparendos/admin/calendario/2026-11-04", new { esFestivo = true, descripcion = "Prueba de auditoría" }, ct)).EnsureSuccessStatusCode();
        var global = await super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/auditoria?global=true&entidad=calendario_dia", ct);
        global.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("entidadId").GetString() == "2026-11-04");
        (await super.PutAsJsonAsync("/api/v1/comparendos/admin/calendario/2026-11-04", new { esFestivo = false }, ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task B8_LaLecturaDelSuperAdmin_QuedaAuditadaEnLaEmpresa_YLaDeLaGestoraNo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item("AUD-1"))).Values.Single();
        var superUsuario = new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true);
        var super = api.Cliente(superUsuario);
        var gestora = new UsuarioDePrueba(empresa, [Permisos.BandejaVer]);

        (await super.GetAsync($"/api/v1/comparendos/comparendos?empresa={empresa}", ct)).EnsureSuccessStatusCode();
        (await super.GetAsync($"/api/v1/comparendos/comparendos/{id}", ct)).EnsureSuccessStatusCode();
        (await api.Cliente(gestora).GetAsync($"/api/v1/comparendos/comparendos/{id}", ct)).EnsureSuccessStatusCode();

        var lecturas = await api.EnDbAsync(db => db.AuditoriasConfiguracion.Where(a => a.TenantId == empresa && a.Accion == "LEER").ToListAsync(ct));
        lecturas.Should().OnlyContain(a => a.Entidad == "lectura_superadmin" && a.UsuarioId != gestora.Id, "la gestora leyendo lo suyo no se audita");
        lecturas = lecturas.Where(a => a.UsuarioId == superUsuario.Id).ToList();
        lecturas.Should().HaveCount(2);
        lecturas.Select(a => a.EntidadId).Should().Contain($"/api/v1/comparendos/comparendos/{id}");
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Portal;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13480 — portal del conductor.</summary>
public sealed class PortalTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private async Task<(Guid Comparendo, string Token)> EmitirAsync()
    {
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item($"PT-{Random.Shared.Next(100000)}")))!.Values.Single();
        await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.ContraventorAsignar])).PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
        {
            tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = "juan@ejemplo.com", declaracionTratamientoDatos = true,
        });
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var c = await db.Comparendos.Include(x => x.Contraventor).SingleAsync(x => x.Id == id);
        var enlace = scope.ServiceProvider.GetRequiredService<ServicioPortal>().Emitir(c);
        await db.SaveChangesAsync();
        return (id, enlace.Url[(enlace.Url.LastIndexOf('/') + 1)..]);
    }

    [Fact]
    public async Task AC1_EnlaceValido_MuestraElComparendoSinDatosSensibles()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (id, token) = await EmitirAsync();

        var portal = await api.Anonimo().GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/publico/portal/{token}", ct);
        portal.GetProperty("conductor").GetProperty("documento").GetString().Should().Be("CC ****4050");
        portal.GetProperty("comparendo").GetProperty("organismo").GetString().Should().Be("SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA");
        portal.GetProperty("descuento").GetProperty("texto").GetString().Should().StartWith("50% de descuento hasta el");
        portal.ToString().Should().NotContain("radicado").And.NotContain("representante");

        await api.Anonimo().GetAsync($"/api/v1/comparendos/publico/portal/{token}", ct);
        (await api.EnDbAsync(db => db.Timeline.CountAsync(t => t.ComparendoId == id && t.Tipo == TipoEventoTimeline.PortalAbierto, ct))).Should().Be(1, "un evento por día y enlace");
        (await api.EnDbAsync(db => db.PortalEnlaces.SingleAsync(p => p.ComparendoId == id, ct))).Accesos.Should().Be(2);
    }

    [Fact]
    public async Task AC2_Revocado_Inventado_YVencido_RespondenLoMismo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (id, token) = await EmitirAsync();
        var empresa = await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == id).Select(c => c.TenantId).SingleAsync(ct));

        (await api.Anonimo().GetAsync("/api/v1/comparendos/publico/portal/inventado", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.AvisosEnviar])).PostAsync($"/api/v1/comparendos/comparendos/{id}/portal/revocar", null, ct)).EnsureSuccessStatusCode();
        var revocado = await api.Anonimo().GetAsync($"/api/v1/comparendos/publico/portal/{token}", ct);
        revocado.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await revocado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("error").GetString().Should().Be("Este enlace no está disponible.");

        var (_, otro) = await EmitirAsync();
        await api.EnDbAsync(db => db.PortalEnlaces.Where(p => p.TokenHash == ServicioPortal.Hash(otro)).ExecuteUpdateAsync(u => u.SetProperty(p => p.ExpiraEn, api.Reloj.Ahora.AddDays(-1))));
        (await api.Anonimo().GetAsync($"/api/v1/comparendos/publico/portal/{otro}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13484 — Centro de Mensajes.</summary>
public sealed class MensajesTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    private async Task<Guid> RegistrarAsync(Guid? empresa, TipoComunicacion tipo)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var correo = scope.ServiceProvider.GetRequiredService<ServicioCorreo>();
        var lista = await correo.RegistrarAsync(new CorreoNuevo
        {
            TenantId = empresa, Tipo = tipo, Plantilla = "comparendos.prueba", Para = [new Destinatario("destino@example.test", "Destino")], Cc = ["copia@example.test"],
            Asunto = "Asunto de prueba", Html = "<html><body><p>Hola</p></body></html>",
        }, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ComparendosDb>().SaveChangesAsync();
        lista.Should().HaveCount(2, "va un correo por destinatario visible (para y CC, S-59)");
        return lista[0].Id;
    }

    [Fact]
    public async Task Listado_AisladoPorEmpresa_YDetalleConHtmlYAvisoDeApertura()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var id = await RegistrarAsync(empresa, TipoComunicacion.Aviso1);
        await RegistrarAsync(null, TipoComunicacion.AlertaFalloIntegracion);
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MensajesVer, Permisos.AvisosEnviar]));

        var propios = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/mensajes", ct);
        propios.GetProperty("total").GetInt32().Should().Be(2);
        var internas = await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true)).GetFromJsonAsync<JsonElement>("/api/v1/comparendos/mensajes?internas=true", ct);
        internas.GetProperty("items").EnumerateArray().Should().OnlyContain(m => m.GetProperty("tipo").GetString() == "AlertaFalloIntegracion");

        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/mensajes/{id}", ct);
        detalle.GetProperty("html").GetString().Should().Contain($"/publico/t/{id:N}.gif");
        detalle.GetProperty("avisoApertura").GetString().Should().Contain("no constituye prueba");
        (await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.MensajesVer])).GetAsync($"/api/v1/comparendos/mensajes/{id}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reintentar_SoloFallidos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var id = await RegistrarAsync(empresa, TipoComunicacion.Aviso2);
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MensajesVer, Permisos.AvisosEnviar]));
        (await cliente.PostAsync($"/api/v1/comparendos/mensajes/{id}/reintentar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await api.EnDbAsync(db => db.Comunicaciones.Where(c => c.Id == id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Estado, EstadoComunicacion.Fallido)));
        var r = await cliente.PostAsync($"/api/v1/comparendos/mensajes/{id}/reintentar", null, ct);
        (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("estado").GetString().Should().Be("Enviado");
    }

    [Fact]
    public async Task D36_FiltroPorCuentaDeSalida_YRemitenteEnLaFila()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var id = await RegistrarAsync(empresa, TipoComunicacion.Aviso1);
        await api.EnDbAsync(db => db.Comunicaciones.Where(c => c.Id == id)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.CanalCorreo, CanalCorreo.EmpresaApi).SetProperty(c => c.RemitenteNombre, "Flota Prueba")));
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MensajesVer]));

        var propia = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/mensajes?canal=EmpresaApi", ct);
        var fila = propia.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        (fila.GetProperty("id").GetGuid(), fila.GetProperty("remitente").GetString()).Should().Be((id, "Flota Prueba"));
        (await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/mensajes?canal=FlitSmtp", ct)).GetProperty("total").GetInt32().Should().Be(1);
        (await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/mensajes?canal=Email", ct)).GetProperty("total").GetInt32().Should().Be(2);
    }
}

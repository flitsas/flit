using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13462 (alertas y log de migración) y el despacho de correos del Centro de Mensajes.</summary>
public sealed class AlertasYCorreoTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    [Fact]
    public async Task AlertaPorFallo_FiltraDestinatarios_RespetaUmbralEInterruptor_YSeDespacha()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        (await Super.PostAsJsonAsync("/api/v1/comparendos/admin/alertas/destinatarios", new { correo = "infra@flit.test", nombre = "Infra", tipoAlerta = "Todas" }, ct)).EnsureSuccessStatusCode();
        (await Super.PostAsJsonAsync("/api/v1/comparendos/admin/alertas/destinatarios", new { correo = "muni@flit.test", nombre = "Municipal", tipoAlerta = "SoloMunicipal" }, ct)).EnsureSuccessStatusCode();
        var empresa = Guid.NewGuid();
        await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoGestionar])).PostAsJsonAsync("/api/v1/comparendos/monitoreo/nits", new { numero = "901234567" }, ct);
        api.Fuente.Respuestas["SimitNit:901234567"] = () => RespuestaFuente.Error(TipoErrorFuente.Timeout, "Sin respuesta en 30 s");

        // Umbral 0: una alerta por clave fallida, solo a quien recibe SIMIT (Todas), no al de solo municipal.
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        var alertas = await api.EnDbAsync(db => db.Comunicaciones.Where(c => c.Tipo == TipoComunicacion.AlertaFalloIntegracion).ToListAsync(ct));
        alertas.Should().ContainSingle();
        alertas[0].Destinatarios.Should().Contain("infra@flit.test").And.NotContain("muni@flit.test");
        alertas[0].Asunto.Should().StartWith("[GDC ALERTA] Fallo en migración SIMIT por NIT");

        // Umbral 5 con 2 fallos (las dos variantes): no se envía consolidado.
        (await Super.PutAsJsonAsync("/api/v1/comparendos/admin/alertas", new { alertasHabilitadas = true, umbralFallosCiclo = 5, plantillaAsunto = "[GDC ALERTA] {FUENTE}", remitenteNombre = "FLIT" }, ct)).EnsureSuccessStatusCode();
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.Tipo == TipoComunicacion.AlertaFalloIntegracion, ct))).Should().Be(1);

        // Interruptor apagado: nada.
        (await Super.PutAsJsonAsync("/api/v1/comparendos/admin/alertas", new { alertasHabilitadas = false, umbralFallosCiclo = 0, plantillaAsunto = "x", remitenteNombre = "FLIT" }, ct)).EnsureSuccessStatusCode();
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.Tipo == TipoComunicacion.AlertaFalloIntegracion, ct))).Should().Be(1);

        // El despachador la envía (sin Notificaciones configurado, por consola) y el píxel registra la apertura.
        var despachador = api.App.Services.GetRequiredService<DespachadorCorreos>();
        await despachador.DespacharPendientesAsync(ct);
        var enviada = await api.EnDbAsync(db => db.Comunicaciones.AsNoTracking().SingleAsync(c => c.Id == alertas[0].Id, ct));
        enviada.Estado.Should().Be(EstadoComunicacion.Enviado);
        (await api.EnDbAsync(db => db.ComunicacionIntentos.CountAsync(i => i.ComunicacionId == enviada.Id, ct))).Should().Be(1);

        var pixel = await api.Anonimo().GetAsync($"/api/v1/comparendos/publico/t/{enviada.Id:N}.gif", ct);
        pixel.Content.Headers.ContentType!.MediaType.Should().Be("image/gif");
        var abierta = await api.EnDbAsync(db => db.Comunicaciones.AsNoTracking().SingleAsync(c => c.Id == enviada.Id, ct));
        abierta.Estado.Should().Be(EstadoComunicacion.Abierto);
        abierta.AbiertoEn.Should().NotBeNull();
    }

    [Fact]
    public async Task LogDeMigracion_FiltraPorEmpresaYExportaExcel()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoGestionar])).PostAsJsonAsync("/api/v1/comparendos/monitoreo/placas", new { placas = new[] { "LOG123" } }, ct);
        await api.CicloAsync(CanalIngesta.SimitPlaca, empresa);

        var pagina = await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/admin/log-migracion?empresa={empresa}", ct);
        pagina.GetProperty("total").GetInt32().Should().Be(1);
        pagina.GetProperty("items")[0].GetProperty("clave").GetString().Should().Be("placa LOG123");

        var excel = await Super.GetAsync($"/api/v1/comparendos/admin/log-migracion/excel?empresa={empresa}", ct);
        excel.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        (await excel.Content.ReadAsByteArrayAsync(ct)).Take(2).Should().Equal((byte)'P', (byte)'K');

        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer])).GetAsync("/api/v1/comparendos/admin/log-migracion", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

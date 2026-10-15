using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>La plataforma rechaza de forma definitiva todo correo a una bandeja de radicación de OT.</summary>
public sealed class EnvioQueRechazaOt : IEnvioCorreo
{
    public Task<ResultadoEnvioCorreo> EnviarAsync(Guid? tenantId, CorreoParaEnviar correo, CancellationToken ct) =>
        Task.FromResult(correo.DestinatarioEmail switch
        {
            var d when d.StartsWith("radicacion@", StringComparison.Ordinal) => new ResultadoEnvioCorreo("RECHAZADO", "Buzón inexistente", false, null),
            var d when d.StartsWith("rebota@", StringComparison.Ordinal) => new ResultadoEnvioCorreo("DESTINATARIO_RECHAZADO", "550 user unknown", false, null),
            var d when d.StartsWith("falla@", StringComparison.Ordinal) => new ResultadoEnvioCorreo("ERROR", "Error del proveedor", false, null),
            _ => new ResultadoEnvioCorreo(ResultadoEnvioCorreo.Enviado, null, false, Guid.NewGuid().ToString()),
        });
}

public sealed class ApiConOtCaido : ApiConFuente
{
    protected override void Reemplazar(IServiceCollection services)
    {
        base.Reemplazar(services);
        services.AddScoped<IEnvioCorreo, EnvioQueRechazaOt>();
    }
}

/// <summary>§6.10 pasos 3-4: si el correo al OT no sale, no hay solicitud ni aviso 4 al conductor.</summary>
public sealed class CambioContraventorFallidoTests(ApiConOtCaido api) : IClassFixture<ApiConOtCaido>
{
    [Fact]
    public async Task CorreoAlOtFallido_NoEnviaAviso4_YVuelveASinSolicitar()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.PatchAsJsonAsync("/api/v1/comparendos/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001", new { correosRadicacion = new[] { "radicacion@movilidad.test" } }, ct)).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, cambio: true);
        var id = (await IngresarAsync(api, empresa, Item("CCF-1"))).Values.Single();
        var cliente = api.Cliente(new UsuarioDePrueba(empresa,
            [Permisos.BandejaVer, Permisos.ContraventorAsignar, Permisos.ContraventorDocumentos, Permisos.CambioContraventorSolicitar, Permisos.RepresentantesGestionar]));
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
        {
            tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = "juan@example.test", declaracionTratamientoDatos = true,
        }, ct)).EnsureSuccessStatusCode();
        foreach (var tipo in new[] { "Cedula", "Licencia" })
        {
            using var pdf = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } }, "archivo", "d.pdf" } };
            (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/documentos?tipo={tipo}", pdf, ct)).EnsureSuccessStatusCode();
        }

        (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", new
        {
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", cargo = "Representante legal",
            permisoEnvioDp = true, permisoCambioContraventor = true,
        }, ct)).EnsureSuccessStatusCode();

        var r = await (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor", null, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        r.GetProperty("correoOt").GetString().Should().Be("Fallido");
        (await api.EnDbAsync(db => db.AvisosConductor.AnyAsync(a => a.ComparendoId == id && a.TipoAviso == TipoAviso.Aviso4, ct))).Should().BeFalse();
        (await api.EnDbAsync(db => db.Comunicaciones.AnyAsync(c => c.ComparendoId == id && c.Tipo == TipoComunicacion.Aviso4, ct))).Should().BeFalse();
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == id, ct))).EstadoCambioContraventor.Should().Be(EstadoCambioContraventor.SinSolicitar);
        (await api.EnDbAsync(db => db.SolicitudesCambio.SingleAsync(s => s.ComparendoId == id, ct))).Estado.Should().Be(EstadoSolicitudCambio.EnvioFallido);
    }

    /// <summary>RN-114: un aviso que no llegó no cuenta; el fallido se reintenta y el rebotado espera un correo nuevo.</summary>
    [Theory]
    [InlineData("falla@example.test", false)]
    [InlineData("rebota@example.test", true)]
    public async Task AvisoNoEntregado_NoCuentaComoEnviado(string correo, bool queda)
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item($"AVF-{correo[..4]}"))).Values.Single();
        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.ContraventorAsignar])).PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
        {
            tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo, declaracionTratamientoDatos = true,
        }, ct)).EnsureSuccessStatusCode();

        await using (var scope = api.App.Services.CreateAsyncScope())
        {
            var ejecutor = scope.ServiceProvider.GetServices<Flit.Comparendos.Api.Modulos.Trabajos.IEjecutorTrabajo>().OfType<Flit.Comparendos.Api.Modulos.Avisos.SchedulerAvisos>().Single();
            await ejecutor.EjecutarAsync(new Flit.Comparendos.Api.Modulos.Trabajos.SolicitudTrabajo(TipoTrabajo.AvisosConductor,
                Disparo.Manual, empresa), ct);
        }

        // Tres intentos para el fallido (1, 5 y 25 min); el rebote es definitivo al primero.
        for (var i = 0; i < 4; i++)
        {
            await api.App.Services.GetRequiredService<DespachadorCorreos>().DespacharPendientesAsync(ct);
            api.Reloj.Ahora = api.Reloj.Ahora.AddMinutes(30);
        }

        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == id, ct))).EstadoNotificacionConductor.Should().Be(EstadoNotificacionConductor.SinNotificar);
        (await api.EnDbAsync(db => db.AvisosConductor.AnyAsync(a => a.ComparendoId == id, ct))).Should().Be(queda);
    }

    /// <summary>Un OT con dos correos de radicación donde uno rebota: el DP queda enviado (el radicado llegó) y el timeline lo dice.</summary>
    [Fact]
    public async Task DpAOtConVariosCorreos_EnviadoSiAlMenosUnoLlega()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.PatchAsJsonAsync("/api/v1/comparendos/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001",
            new { correosRadicacion = new[] { "radicacion@movilidad.test", "judicial@movilidad.test" } }, ct)).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, dp: true, requiereRl: false);
        var dc = (await super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/tipologias", ct)).EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == "DC").GetProperty("id").GetGuid();
        (await super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/tipologias/{dc}", new { habilitada = true }, ct)).EnsureSuccessStatusCode();
        var id = (await IngresarAsync(api, empresa, Item("VC-1", notificacion: new DateOnly(2025, 9, 1)))).Values.Single();
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer, Permisos.DpGenerar, Permisos.DpEnviar]));

        var r = await (await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/dp", new { tipologiaId = dc }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        r.GetProperty("estado").GetString().Should().Be("Enviado");
        var dp = await api.EnDbAsync(db => db.DerechosPeticion.SingleAsync(d => d.ComparendoId == id, ct));
        dp.ComunicacionIds.Should().HaveCount(2);
        (await api.EnDbAsync(db => db.Timeline.SingleAsync(t => t.ComparendoId == id && t.Tipo == TipoEventoTimeline.DpEnviado, ct)))
            .Descripcion.Should().Contain("1 de 2 buzones del organismo no lo recibieron");
        await super.PatchAsJsonAsync("/api/v1/comparendos/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001",
            new { correosRadicacion = new[] { "radicacion@movilidad.test" } }, ct);
    }

    /// <summary>§6.17: si el correo al OT de un ítem falla, el lote termina con errores aunque el DP se haya creado.</summary>
    [Fact]
    public async Task LoteDp_CorreoAlOtFallido_QuedaCompletadoConErrores()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.PatchAsJsonAsync("/api/v1/comparendos/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001", new { correosRadicacion = new[] { "radicacion@movilidad.test" } }, ct)).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, dp: true);
        var dc = (await super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/tipologias", ct)).EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == "DC").GetProperty("id").GetGuid();
        (await super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/tipologias/{dc}", new { habilitada = true }, ct)).EnsureSuccessStatusCode();
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer, Permisos.DpGenerar, Permisos.DpEnviar, Permisos.DpEnviarMasivo, Permisos.RepresentantesGestionar]));
        var rl = await (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", new
        {
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", cargo = "Representante legal",
            direccionNotificacion = "Calle 100 # 9A-45", permisoEnvioDp = true, permisoCambioContraventor = true,
        }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        using var firma = new MultipartFormDataContent { { new ByteArrayContent(Convert.FromBase64String(png)) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "archivo", "firma.png" } };
        (await cliente.PostAsync($"/api/v1/comparendos/representantes/{rl.GetProperty("id").GetGuid()}/documentos/firma", firma, ct)).EnsureSuccessStatusCode();
        var ids = await IngresarAsync(api, empresa, Item("LF-1", notificacion: new DateOnly(2025, 9, 1)));

        var lote = await (await cliente.PostAsJsonAsync("/api/v1/comparendos/dp/lotes", new { tipologiaId = dc, comparendoIds = ids.Values }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        var procesador = api.App.Services.GetRequiredService<Flit.Comparendos.Api.Modulos.DerechosPeticion.ProcesadorLotesDp>();
        while (await procesador.ProcesarSiguienteAsync(ct))
        {
        }

        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/dp/lotes/{lote.GetProperty("id").GetGuid()}", ct);
        (detalle.GetProperty("estado").GetString(), detalle.GetProperty("enviados").GetInt32(), detalle.GetProperty("fallidos").GetInt32())
            .Should().Be(("CompletadoConErrores", 0, 1));
    }
}

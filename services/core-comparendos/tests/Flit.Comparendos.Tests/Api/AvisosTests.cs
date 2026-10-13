using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Avisos;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13485 — avisos 1 a 3 al conductor. Fotomulta notificada el 1-oct-2026: 50% hasta el 19-oct, 25% hasta el 10-nov.</summary>
public sealed class AvisosTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly string[] Operador = [Permisos.BandejaVer, Permisos.ContraventorAsignar, Permisos.AvisosEnviar, Permisos.PagosRegistrar];

    private async Task<(Guid Empresa, Dictionary<string, Guid> Ids)> PrepararAsync(params string[] numeros)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        var ids = await IngresarAsync(api, empresa, numeros.Select(n => Item(n)).ToArray());
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        foreach (var id in ids.Values)
        {
            (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
            {
                tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = "juan@ejemplo.com", declaracionTratamientoDatos = true,
            })).EnsureSuccessStatusCode();
        }

        return (empresa, ids);
    }

    private async Task SchedulerAsync(Guid empresa)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var ejecutor = scope.ServiceProvider.GetServices<IEjecutorTrabajo>().OfType<SchedulerAvisos>().Single();
        await ejecutor.EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.AvisosConductor, Disparo.Manual, empresa), CancellationToken.None);
    }

    private Task<List<AvisoDto>> AvisosAsync(Guid id) =>
        api.EnDbAsync(db => db.AvisosConductor.Where(a => a.ComparendoId == id).OrderBy(a => a.RegistradoEn).Select(a => new AvisoDto(a.TipoAviso, a.VentanaAlEnvio, a.PorcentajeDescuento)).ToListAsync());

    private sealed record AvisoDto(TipoAviso Tipo, VentanaDescuento Ventana, short Porcentaje);

    [Fact]
    public async Task AC1_CicloCompleto_Aviso1_Aviso2_Aviso3_SinRetroactividad()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, ids) = await PrepararAsync("AV-1");
        var id = ids["AV1"];

        await SchedulerAsync(empresa);
        await SchedulerAsync(empresa);
        (await AvisosAsync(id)).Should().Equal(new AvisoDto(TipoAviso.Aviso1, VentanaDescuento.Descuento50, 50));
        var c = await api.EnDbAsync(db => db.Comparendos.SingleAsync(x => x.Id == id));
        c.EstadoNotificacionConductor.Should().Be(EstadoNotificacionConductor.Aviso1);
        (await api.EnDbAsync(db => db.PortalEnlaces.CountAsync(p => p.ComparendoId == id))).Should().Be(1);
        var correo = await api.EnDbAsync(db => db.Comunicaciones.SingleAsync(x => x.ComparendoId == id));
        correo.Asunto.Should().Be("Comparendo AV-1 del vehículo ABC124");
        correo.Tipo.Should().Be(TipoComunicacion.Aviso1);

        api.Reloj.Hoy(new DateOnly(2026, 10, 20));
        await SchedulerAsync(empresa);
        api.Reloj.Hoy(new DateOnly(2026, 11, 11));
        await SchedulerAsync(empresa);

        (await AvisosAsync(id)).Select(a => (a.Tipo, a.Porcentaje)).Should().Equal((TipoAviso.Aviso1, (short)50), (TipoAviso.Aviso2, (short)25), (TipoAviso.Aviso3, (short)0));
    }

    [Fact]
    public async Task AC2_Pagado_NoRecibeAvisos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, ids) = await PrepararAsync("AV-2");
        await api.EnDbAsync(db => db.Comparendos.Where(x => x.Id == ids["AV2"]).ExecuteUpdateAsync(u => u.SetProperty(x => x.Pagado, true)));

        await SchedulerAsync(empresa);

        (await AvisosAsync(ids["AV2"])).Should().BeEmpty();
        var r = await api.Cliente(new UsuarioDePrueba(empresa, Operador)).PostAsJsonAsync($"/api/v1/comparendos/comparendos/{ids["AV2"]}/avisos", new { tipo = "Aviso1" }, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("error").GetString().Should().Be("El comparendo ya está pagado.");
    }

    [Fact]
    public async Task Manual_SeEnviaYSeDespachaAlMomento()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, ids) = await PrepararAsync("AV-3");
        var r = await api.Cliente(new UsuarioDePrueba(empresa, Operador)).PostAsJsonAsync($"/api/v1/comparendos/comparendos/{ids["AV3"]}/avisos", new { tipo = "Aviso2" }, TestContext.Current.CancellationToken);

        (await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("estado").GetString().Should().Be("Enviado");
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(x => x.Id == ids["AV3"]))).EstadoNotificacionConductor.Should().Be(EstadoNotificacionConductor.Aviso2);
    }

    [Theory]
    [InlineData("07:00", "07:00", true)]
    [InlineData("07:00", "07:14", true)]
    [InlineData("07:00", "07:15", false)]
    [InlineData("07:00", "06:59", false)]
    public void EnTramo(string hora, string ahora, bool esperado) =>
        SchedulerAvisos.EnTramo(TimeOnly.Parse(hora, System.Globalization.CultureInfo.InvariantCulture), TimeOnly.Parse(ahora, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(esperado);

    [Fact]
    public async Task S33_ConductorReemplazado_RecibeSusPropiosAvisosAutomaticos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, ids) = await PrepararAsync("AV-R");
        var id = ids["AVR"];
        await SchedulerAsync(empresa);
        (await AvisosAsync(id)).Should().ContainSingle();

        (await api.Cliente(new UsuarioDePrueba(empresa, Operador)).PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
        {
            tipoDocumento = "CC", numeroDocumento = "79888777", nombres = "Ana Ruiz", correo = "ana@ejemplo.com", motivo = "El contrato era de otra conductora", declaracionTratamientoDatos = true,
        }, ct)).EnsureSuccessStatusCode();
        api.Reloj.Hoy(new DateOnly(2026, 10, 13));
        await SchedulerAsync(empresa);
        var destinos = await api.EnDbAsync(db => db.AvisosConductor.Where(a => a.ComparendoId == id && a.TipoAviso == TipoAviso.Aviso1).Select(a => a.CorreoDestino).ToListAsync(ct));
        destinos.Should().BeEquivalentTo("juan@ejemplo.com", "ana@ejemplo.com");
    }

    [Fact]
    public async Task D7_LaFotoDeLaFotomulta_VaAdjunta_YSiNoSePuedeTraer_ElAvisoSaleIgual()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        api.Fotos.PorUrl["https://simit.test/foto/AV-F1.jpg"] = [0xFF, 0xD8, 0xFF, 0xE0];
        var ids = await IngresarAsync(api, empresa,
            Item("AV-F1") with { FotoEvidenciaUrl = "https://simit.test/foto/AV-F1.jpg" },
            Item("AV-F2") with { FotoEvidenciaUrl = "https://simit.test/foto/no-existe.jpg" });
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        foreach (var id in ids.Values)
        {
            (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
            {
                tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = "juan@ejemplo.com", declaracionTratamientoDatos = true,
            }, ct)).EnsureSuccessStatusCode();
        }

        await SchedulerAsync(empresa);

        async Task<List<string>> Adjuntos(Guid id) => await api.EnDbAsync(db =>
            (from c in db.Comunicaciones where c.ComparendoId == id join a in db.ComunicacionAdjuntos on c.Id equals a.ComunicacionId select a.Nombre).ToListAsync(ct));
        (await Adjuntos(ids["AVF1"])).Should().Equal("fotomulta-AV-F1.jpg");
        (await AvisosAsync(ids["AVF2"])).Should().ContainSingle("sin la foto el aviso sale igual");
        (await Adjuntos(ids["AVF2"])).Should().BeEmpty();
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.5", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("200.21.70.10", true)]
    public void D7_SoloSeDescargaDeDireccionesPublicas(string ip, bool publica) =>
        DescargaFotoHttp.EsPublica(System.Net.IPAddress.Parse(ip)).Should().Be(publica);
}

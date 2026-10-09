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
}

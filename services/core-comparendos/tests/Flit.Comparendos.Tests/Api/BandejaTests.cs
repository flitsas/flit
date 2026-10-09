using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// HU #13496 (bandeja) y HU #13497 (detalle). Hoy = viernes 9-oct-2026 (festivo el lunes 12).
/// A: notificado 1-oct → límite 19-oct (5 hábiles, en tiempo). B: 23-sep → límite 8-oct (vencido).
/// C: 25-sep → límite 13-oct (1 hábil: próximo a vencer). D: fotomulta sin notificación.
/// </summary>
public sealed class BandejaTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly string[] Lectura = [Permisos.BandejaVer, Permisos.BandejaExportar, Permisos.ComparendoObservar, Permisos.ComparendoInactivar];

    private static ItemFuente Item(string numero, DateOnly? notificacion, decimal valor) => new()
    {
        Numero = numero, Placa = "BND123", FechaImposicion = new DateOnly(2026, 9, 1), FechaNotificacion = notificacion, EsFotomulta = true,
        Secretaria = "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA", ValorTotal = valor, CodigoInfraccion = "C29",
    };

    private async Task<(Guid Empresa, HttpClient Cliente)> PrepararAsync()
    {
        var empresa = Guid.NewGuid();
        var nit = $"9{Random.Shared.Next(10000000, 99999999)}";
        await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoGestionar])).PostAsJsonAsync("/api/v1/comparendos/monitoreo/nits", new { numero = nit }, TestContext.Current.CancellationToken);
        api.Fuente.Respuestas[$"SimitNit:{nit}"] = () => RespuestaFuente.Exitosa(
        [
            Item("A-1", new DateOnly(2026, 10, 1), 100000m), Item("B-2", new DateOnly(2026, 9, 23), 200000m),
            Item("C-3", new DateOnly(2026, 9, 25), 300000m), Item("D-4", null, 400000m),
        ]);
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        return (empresa, api.Cliente(new UsuarioDePrueba(empresa, Lectura)));
    }

    private static async Task<List<string>> Numeros(HttpClient cliente, string query) =>
        (await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos?{query}")).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("numero").GetString()!).ToList();

    [Fact]
    public async Task HU13496_FiltroPorEstadoLegal_YOrdenPorUrgencia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente) = await PrepararAsync();

        (await Numeros(cliente, "estadoLegal=EnTiempo")).Should().Equal("C-3", "A-1");
        (await Numeros(cliente, "estadoLegal=Vencido")).Should().Equal("B-2");
        (await Numeros(cliente, "estadoLegal=SinNotificar")).Should().Equal("D-4");
        (await Numeros(cliente, string.Empty)).Take(2).Should().Equal("C-3", "A-1");
    }

    [Fact]
    public async Task HU13496_AC1_ProximosAVencer_YContadores()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente) = await PrepararAsync();

        (await Numeros(cliente, "rapido=ProximosAVencer")).Should().Equal("C-3");
        var contadores = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos/contadores", TestContext.Current.CancellationToken);
        contadores.GetProperty("total").GetInt32().Should().Be(4);
        contadores.GetProperty("proximosAVencer").GetInt32().Should().Be(1);
        contadores.GetProperty("sinContraventor").GetInt32().Should().Be(4, "la integración por defecto es manual y nadie tiene conductor");
    }

    [Fact]
    public async Task HU13496_FilaConCalculos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente) = await PrepararAsync();
        var pagina = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos?numero=a1", TestContext.Current.CancellationToken);
        var fila = pagina.GetProperty("items")[0];

        fila.GetProperty("estadoLegal").GetString().Should().Be("EnTiempo");
        fila.GetProperty("diasHabilesRestantes").GetInt32().Should().Be(5);
        fila.GetProperty("ventanaDescuento").GetString().Should().Be("Descuento50");
        fila.GetProperty("organismo").GetString().Should().Be("SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA");
        fila.GetProperty("estadoProceso").GetString().Should().Be("SinProcesar");
    }

    [Fact]
    public async Task HU13496_AC2_ExportaLoFiltrado()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente) = await PrepararAsync();
        var r = await cliente.GetAsync("/api/v1/comparendos/comparendos/excel?estadoLegal=EnTiempo", TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentDisposition!.FileName.Should().StartWith("comparendos-");
    }

    [Fact]
    public async Task HU13496_SuperAdmin_EligeGestora_YOtraEmpresaNoVe()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, _) = await PrepararAsync();
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

        (await super.GetAsync("/api/v1/comparendos/comparendos", ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos?empresa={empresa}", ct)).GetProperty("total").GetInt32().Should().Be(4);
        (await Numeros(api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.BandejaVer])), string.Empty)).Should().BeEmpty();
    }

    [Fact]
    public async Task HU13497_Detalle_Observacion_EInactivacion()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente) = await PrepararAsync();
        var id = await api.EnDbAsync(db => db.Comparendos.Where(c => c.TenantId == empresa && c.NumeroNormalizado == "C3").Select(c => c.Id).SingleAsync(ct));

        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}", ct);
        detalle.GetProperty("situacion").GetProperty("proximoAVencer").GetBoolean().Should().BeTrue();
        detalle.GetProperty("fuentes").GetArrayLength().Should().Be(1);

        (await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/observaciones", new { texto = "Llamé al conductor" }, ct)).EnsureSuccessStatusCode();
        var linea = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}/timeline", ct);
        linea[0].GetProperty("tipo").GetString().Should().Be("NotaManual");
        linea[0].GetProperty("actorNombre").GetString().Should().Be("operador@empresa.test");

        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/inactivar", new { motivo = "Prueba del SuperAdmin" }, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/inactivar", new { motivo = "no" }, ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/inactivar", new { motivo = "Duplicado de otro registro" }, ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Numeros(cliente, string.Empty)).Should().NotContain("C-3");
        var inactivo = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}", ct);
        inactivo.GetProperty("estadoProceso").GetString().Should().Be("Inactivado");
    }
}

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
        var (empresa, cliente) = await PrepararAsync();

        (await Numeros(cliente, "estadoLegal=EnTiempo")).Should().Equal("C-3", "A-1");
        (await Numeros(cliente, "estadoLegal=Vencido")).Should().Equal("B-2");
        (await Numeros(cliente, "estadoLegal=SinNotificar")).Should().Equal("D-4");
        (await Numeros(cliente, string.Empty)).Take(2).Should().Equal("C-3", "A-1");

        var monitoreo = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoVer]));
        (await monitoreo.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/monitoreo/nits", TestContext.Current.CancellationToken)).EnumerateArray().Single()
            .GetProperty("comparendosActivos").GetInt32().Should().Be(4, "§7.12: la tabla de NITs dice cuántos comparendos activos tiene cada uno");
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

    [Fact]
    public async Task E4_MisExportaciones_MuestranLosFiltrosConQueSePidieron()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var usuario = new UsuarioDePrueba(empresa, Lectura);
        var bogota = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
        var filtros = new Flit.Comparendos.Api.Modulos.Bandeja.FiltrosBandeja { Placa = "ABC123", Organismos = [bogota], Rapido = "Radar" };
        await api.EnDbAsync(async db =>
        {
            db.Exportaciones.Add(new Flit.Comparendos.Api.Persistence.Modelo.Exportacion
            {
                Id = Guid.NewGuid(), TenantId = empresa, SolicitadaPor = usuario.Id, CreadoEn = DateTimeOffset.UtcNow,
                Filtros = Flit.Comparendos.Api.Modulos.Bandeja.ExportacionBandeja.Serializar(empresa, filtros, usuario.Email),
            });
            await db.SaveChangesAsync(ct);
        });

        var e = (await api.Cliente(usuario).GetFromJsonAsync<JsonElement>("/api/v1/comparendos/exportaciones", ct)).EnumerateArray().Single();
        e.GetProperty("filtros").GetProperty("placa").GetString().Should().Be("ABC123");
        e.GetProperty("filtros").GetProperty("rapido").GetString().Should().Be("Radar");
        e.GetProperty("nombres").GetProperty(bogota.ToString()).GetString().Should().NotBeNullOrWhiteSpace("el organismo se muestra con su nombre");
    }

    [Fact]
    public async Task HU13870_AC1_BuscaEnNumeroYPlacaALaVez()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente) = await PrepararAsync();
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.TenantId == empresa && c.NumeroNormalizado == "C3").ExecuteUpdateAsync(u => u.SetProperty(c => c.Placa, "XYZ987"), ct));

        (await Numeros(cliente, "buscar=a-1")).Should().Equal("A-1");
        (await Numeros(cliente, "buscar=xyz")).Should().Equal(["C-3"], "la placa también se busca");
        (await Numeros(cliente, "buscar=bnd")).Should().BeEquivalentTo("A-1", "B-2", "D-4");
        (await Numeros(cliente, "buscar=no-existe")).Should().BeEmpty();
    }

    [Fact]
    public async Task HU13868_AC3_ListasPegadas_YNoEs()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente) = await PrepararAsync();
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.TenantId == empresa && c.NumeroNormalizado == "C3").ExecuteUpdateAsync(u => u.SetProperty(c => c.Placa, "XYZ987"), ct));
        var bogota = await api.EnDbAsync(db => db.Comparendos.Where(c => c.TenantId == empresa).Select(c => c.OrganismoTransitoId).FirstAsync(ct));

        (await Numeros(cliente, "numeros=a-1,B2")).Should().BeEquivalentTo("A-1", "B-2");
        (await Numeros(cliente, "numeros=A1")).Should().Equal("A-1");
        (await Numeros(cliente, "numeros=A")).Should().BeEmpty("la lista pegada coincide completa, no por parte");
        (await Numeros(cliente, "numeros=A-1,B-2&negar=numeros")).Should().BeEquivalentTo("C-3", "D-4");
        (await Numeros(cliente, "placas=xyz987")).Should().Equal("C-3");
        (await Numeros(cliente, "placas=xyz987&negar=placas")).Should().BeEquivalentTo("A-1", "B-2", "D-4");
        (await Numeros(cliente, $"organismos={bogota}&negar=organismos")).Should().BeEmpty();
        (await Numeros(cliente, "fuente=Ocr&negar=fuente")).Should().HaveCount(4);
        (await Numeros(cliente, "fuente=Simit&negar=fuente")).Should().BeEmpty();
        (await Numeros(cliente, "negar=fuente")).Should().HaveCount(4, "«no es» sin valores no filtra");
    }

    [Fact]
    public async Task HU13869_AC1_ConteoPorEstadoLegal_RespetaLosDemasFiltros()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente) = await PrepararAsync();
        async Task<Dictionary<string, int>> Estados(string query) =>
            (await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/contadores?{query}", ct)).GetProperty("estadosLegales")
                .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());

        (await Estados(string.Empty)).Should().BeEquivalentTo(new Dictionary<string, int> { ["SinNotificar"] = 1, ["EnTiempo"] = 2, ["Vencido"] = 1, ["Caducado"] = 0, ["Prescrito"] = 0 });
        (await Estados("estadoLegal=Vencido"))["EnTiempo"].Should().Be(2, "cada tarjeta cuenta sin el filtro de estado legal");
        var proximos = await Estados("rapido=ProximosAVencer");
        proximos["EnTiempo"].Should().Be(1, "el atajo elegido sí se respeta");
        proximos["Vencido"].Should().Be(0);
        (await Estados("buscar=a1"))["EnTiempo"].Should().Be(1);
    }
}

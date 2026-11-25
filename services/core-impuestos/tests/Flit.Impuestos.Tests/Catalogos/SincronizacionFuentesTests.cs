using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Configuracion;
using Flit.Impuestos.Tests.Credenciales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Impuestos.Tests.Catalogos;

/// <summary>
/// HU #14694 (M08, plan §4.1 y §5.1, API §4) — <c>GET /fuentes</c> (permiso <c>impuestos.configuracion.ver</c>): los departamentos
/// del proveedor desde la caché global <c>impuestos.fuentes_api</c>, que se refresca con <c>GET /api/v1/fuentes</c> y la api_key de
/// la compañía cuando no está vigente (6 h por defecto). Exige el aprovisionamiento en <c>LISTO</c> o <c>LISTO_SIN_WEBHOOK</c> (si no,
/// 409 <c>APROVISIONAMIENTO_NO_LISTO</c> sin llamar al API). Si el API no responde y no hay caché vigente, 503
/// <c>PROVEEDOR_NO_DISPONIBLE</c>. Al refrescar retira lo que el proveedor ya no trae.
/// </summary>
/// <example>
/// <code>
/// var r = await api.Cliente(token).GetAsync(Program.Prefijo + "/fuentes", ct); // { items: [{ codigo, nombre, activa }] }
/// </code>
/// </example>
public sealed class SincronizacionFuentesTests
{
    public const string Ruta = Program.Prefijo + "/fuentes";

    [Theory]
    [InlineData(EstadoAprovisionamiento.Listo)]
    [InlineData(EstadoAprovisionamiento.ListoSinWebhook)]
    public async Task Con_la_compania_lista_consulta_fuentes_con_su_api_key_y_guarda_la_cache(EstadoAprovisionamiento estado)
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await CargaManualCredencialesTests.ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await ConfiguracionTests.SembrarListaAsync(api, empresa, ct, estado);

        using var r = await api.Cliente(ConfiguracionTests.TokenAdmin(api, empresa)).GetAsync(Ruta, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => (i.GetProperty("codigo").GetString(), i.GetProperty("nombre").GetString(), i.GetProperty("activa").GetBoolean()))
            .Should().Equal(("05", "Antioquia", true), ("41", "Huila", false));
        var pedido = doble.Pedidos.Should().ContainSingle().Subject;
        pedido.Uri.Should().EndWith("/api/v1/fuentes");
        pedido.Cabeceras["X-API-Key"].Should().Be(ClienteApiLiquidacionTests.ApiKey, "se consulta con la api_key de la compañía");
        await using var db = api.Contexto(null);
        var cache = await db.Set<FuenteApi>().AsNoTracking().OrderBy(f => f.DepartamentoCodigo).ToListAsync(ct);
        cache.Select(f => f.DepartamentoCodigo).Should().Equal("05", "41");
        JsonDocument.Parse(cache[0].ParamsSchema).RootElement.GetProperty("type").GetString().Should().Be("object", "params_schema se guarda para el constructor (M09)");
    }

    [Fact]
    public async Task Con_cache_vigente_no_vuelve_a_llamar_al_api()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await CargaManualCredencialesTests.ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await ConfiguracionTests.SembrarListaAsync(api, empresa, ct);
        var cliente = api.Cliente(ConfiguracionTests.TokenAdmin(api, empresa));

        using (var primera = await cliente.GetAsync(Ruta, ct))
            primera.StatusCode.Should().Be(HttpStatusCode.OK);
        using var segunda = await cliente.GetAsync(Ruta, ct);

        segunda.StatusCode.Should().Be(HttpStatusCode.OK);
        doble.Pedidos.Should().ContainSingle("la caché es global y vale 6 horas");
    }

    [Fact]
    public async Task Con_cache_vencida_refresca_y_retira_lo_que_el_proveedor_ya_no_trae()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await CargaManualCredencialesTests.ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await ConfiguracionTests.SembrarListaAsync(api, empresa, ct);
        await using (var semilla = api.Contexto(null))
        {
            var vieja = DateTimeOffset.UtcNow.AddHours(-7);
            semilla.Add(new FuenteApi { DepartamentoCodigo = "99", Nombre = "Retirado", Activa = true, ParamsSchema = "{}", SincronizadoEn = vieja });
            semilla.Add(new FuenteApi { DepartamentoCodigo = "05", Nombre = "Nombre viejo", Activa = false, ParamsSchema = "{}", SincronizadoEn = vieja });
            await semilla.SaveChangesAsync(ct);
        }

        using var r = await api.Cliente(ConfiguracionTests.TokenAdmin(api, empresa)).GetAsync(Ruta, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        doble.Pedidos.Should().ContainSingle();
        await using var db = api.Contexto(null);
        var cache = await db.Set<FuenteApi>().AsNoTracking().OrderBy(f => f.DepartamentoCodigo).ToListAsync(ct);
        cache.Select(f => (f.DepartamentoCodigo, f.Nombre, f.Activa)).Should().Equal(("05", "Antioquia", true), ("41", "Huila", false));
        cache.Should().OnlyContain(f => f.SincronizadoEn > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Un_departamento_de_una_cifra_se_completa_y_sin_active_queda_inactivo()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba
        {
            Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.OK,
                "{\"items\":[{\"id\":\"f-8\",\"name\":null,\"department_code\":\"8\",\"department_name\":\"ATLANTICO\",\"active\":null,\"params_schema\":null}," +
                "{\"id\":\"f-x\",\"name\":\"Raro\",\"department_code\":\"123\",\"department_name\":\"X\",\"active\":true,\"params_schema\":{}}]}"),
        };
        await using var api = await CargaManualCredencialesTests.ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await ConfiguracionTests.SembrarListaAsync(api, empresa, ct);

        using var r = await api.Cliente(ConfiguracionTests.TokenAdmin(api, empresa)).GetAsync(Ruta, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        var item = json.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle("un código que no es de 2 cifras no entra").Subject;
        item.GetProperty("codigo").GetString().Should().Be("08");
        item.GetProperty("nombre").GetString().Should().Be("ATLANTICO", "sin name se usa department_name");
        item.GetProperty("activa").GetBoolean().Should().BeFalse("sin active no se ofrece el departamento");
        await using var db = api.Contexto(null);
        (await db.Set<FuenteApi>().AsNoTracking().SingleAsync(ct)).ParamsSchema.Should().Be("{}");
    }

    [Theory]
    [InlineData(EstadoAprovisionamiento.Pendiente)]
    [InlineData(EstadoAprovisionamiento.RequiereCredenciales)]
    [InlineData(EstadoAprovisionamiento.Incierto)]
    [InlineData(EstadoAprovisionamiento.Suspendido)]
    public async Task Sin_aprovisionamiento_listo_es_409_y_no_llama_al_api(EstadoAprovisionamiento estado)
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await CargaManualCredencialesTests.ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct, estado);

        using var r = await api.Cliente(ConfiguracionTests.TokenAdmin(api, empresa)).GetAsync(Ruta, ct);
        using var sinFila = await api.Cliente(ConfiguracionTests.TokenAdmin(api, Guid.NewGuid())).GetAsync(Ruta, ct);

        await ConfiguracionTests.DebeSerRegla(r, HttpStatusCode.Conflict, "APROVISIONAMIENTO_NO_LISTO",
            "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.", null, ct);
        sinFila.StatusCode.Should().Be(HttpStatusCode.Conflict);
        doble.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Si_el_proveedor_falla_sin_cache_vigente_es_503_PROVEEDOR_NO_DISPONIBLE()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba { Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.InternalServerError, "{}") };
        await using var api = await CargaManualCredencialesTests.ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await ConfiguracionTests.SembrarListaAsync(api, empresa, ct);

        using var r = await api.Cliente(ConfiguracionTests.TokenAdmin(api, empresa)).GetAsync(Ruta, ct);

        await ConfiguracionTests.DebeSerRegla(r, HttpStatusCode.ServiceUnavailable, "PROVEEDOR_NO_DISPONIBLE", "El proveedor no responde. Intenta más tarde.", null, ct);
        await using var db = api.Contexto(null);
        (await db.Set<FuenteApi>().CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task Sin_permiso_de_configuracion_es_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba();
        await using var api = await CargaManualCredencialesTests.ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await ConfiguracionTests.SembrarListaAsync(api, empresa, ct);

        using var r = await api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.LiquidacionesVer])).GetAsync(Ruta, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        doble.Pedidos.Should().BeEmpty();
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>Identidad falsa: todas las empresas tienen el producto salvo las que se apaguen.</summary>
public sealed class ProductosDePrueba : IProductosHabilitados
{
    public ConcurrentDictionary<Guid, bool> Apagadas { get; } = new();

    public Task<bool> ComparendosHabilitadoAsync(Guid tenant, CancellationToken ct) => Task.FromResult(!Apagadas.ContainsKey(tenant));
}

public sealed class ApiConProductos : ApiConFuente
{
    public ProductosDePrueba Productos { get; } = new();

    protected override void Reemplazar(IServiceCollection services)
    {
        base.Reemplazar(services);
        services.AddSingleton<IProductosHabilitados>(Productos);
    }
}

/// <summary>HU #13521 — configurador de consultas por empresa y activación controlada.</summary>
public sealed class ConsultasEmpresaTests(ApiConProductos api) : IClassFixture<ApiConProductos>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    private async Task<(Guid Empresa, string Nit)> EmpresaConNitAsync()
    {
        var empresa = Guid.NewGuid();
        (await Super.PostAsJsonAsync("/api/v1/comparendos/admin/empresas", new { id = empresa, nombre = "Gestora" })).EnsureSuccessStatusCode();
        var nit = $"8{Random.Shared.Next(10000000, 99999999)}";
        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoGestionar])).PostAsJsonAsync("/api/v1/comparendos/monitoreo/nits", new { numero = nit })).EnsureSuccessStatusCode();
        return (empresa, nit);
    }

    private Task<HttpResponseMessage> Canal(Guid empresa, string canal, object cuerpo) =>
        Super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/consultas/canales/{canal}", cuerpo);

    private static object Horas(string proveedor = "prueba", bool encendido = true) =>
        new { encendido, proveedor, modo = "HorasFijas", horas = new[] { "13:00", "07:00" }, soloDiasHabiles = true };

    private async Task<Guid> ProgramadoAsync()
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Flit.Comparendos.Api.Modulos.Ingesta.CicloDeIngesta>()
            .EjecutarAsync(CanalIngesta.SimitNit, Disparo.Programado, null, null, CancellationToken.None);
    }

    [Fact]
    public async Task AC1_SoloSeConsultaConLaConfiguracionActiva()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Reloj.Hoy(new DateOnly(2026, 10, 13));
        var (empresa, nit) = await EmpresaConNitAsync();
        (await Canal(empresa, "SimitNit", Horas())).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await ProgramadoAsync();
        api.Fuente.Llamadas.Keys.Should().NotContain(k => k.EndsWith(nit, StringComparison.Ordinal), "en borrador no se consulta");

        (await Super.PostAsync($"/api/v1/comparendos/admin/empresas/{empresa}/consultas/activar", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ProgramadoAsync();
        api.Fuente.Llamadas.Keys.Should().Contain($"SimitNit:{nit}");
        (await api.EnDbAsync(db => db.ConfiguracionConsultaCanales.SingleAsync(c => c.TenantId == empresa && c.Canal == CanalIngesta.SimitNit, Ct))).UltimoResultado.Should().NotBeNull();

        (await Super.PostAsync($"/api/v1/comparendos/admin/empresas/{empresa}/consultas/pausar", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        api.Fuente.Llamadas.Clear();
        await ProgramadoAsync();
        api.Fuente.Llamadas.Keys.Should().NotContain($"SimitNit:{nit}", "pausada no se consulta");
    }

    [Fact]
    public async Task AC2_ProductoApagado_SaleDeLosCiclos_YNoSePuedeActivar()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, nit) = await EmpresaConNitAsync();
        (await Canal(empresa, "SimitNit", Horas())).EnsureSuccessStatusCode();
        (await Super.PostAsync($"/api/v1/comparendos/admin/empresas/{empresa}/consultas/activar", null, Ct)).EnsureSuccessStatusCode();

        api.Productos.Apagadas[empresa] = true;
        api.Fuente.Llamadas.Clear();
        await ProgramadoAsync();
        api.Fuente.Llamadas.Keys.Should().NotContain($"SimitNit:{nit}");
        var activar = await Super.PostAsync($"/api/v1/comparendos/admin/empresas/{empresa}/consultas/activar", null, Ct);
        activar.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await activar.Content.ReadAsStringAsync(Ct)).Should().Contain("PRODUCTO_NO_HABILITADO");
        var manual = await Super.PostAsJsonAsync("/api/v1/comparendos/admin/ingesta/ejecutar", new { canal = "SimitNit", empresaId = empresa }, Ct);
        manual.StatusCode.Should().Be(HttpStatusCode.Conflict);
        api.Productos.Apagadas.TryRemove(empresa, out _);
    }

    [Fact]
    public async Task AC3_CadaEmpresaConSuProveedor()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (a, nitA) = await EmpresaConNitAsync();
        var (b, nitB) = await EmpresaConNitAsync();
        (await Canal(a, "SimitNit", Horas("prueba"))).EnsureSuccessStatusCode();
        (await Canal(b, "SimitNit", Horas("otra"))).EnsureSuccessStatusCode();
        foreach (var e in new[] { a, b })
            (await Super.PostAsync($"/api/v1/comparendos/admin/empresas/{e}/consultas/activar", null, Ct)).EnsureSuccessStatusCode();
        await ProgramadoAsync();
        (api.Fuente.ProveedorUsado[$"SimitNit:{nitA}"], api.Fuente.ProveedorUsado[$"SimitNit:{nitB}"]).Should().Be(("prueba", "otra"));
    }

    [Fact]
    public async Task AC4_LaEmpresaVeLaAgenda_SinProveedores()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Reloj.Hoy(new DateOnly(2026, 10, 13));
        var (empresa, _) = await EmpresaConNitAsync();
        (await Canal(empresa, "SimitNit", Horas())).EnsureSuccessStatusCode();
        (await Super.PostAsync($"/api/v1/comparendos/admin/empresas/{empresa}/consultas/activar", null, Ct)).EnsureSuccessStatusCode();

        var texto = await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.ConfiguracionVer])).GetStringAsync("/api/v1/comparendos/configuracion/consultas", Ct);
        texto.Should().NotContainAny("proveedor", "prueba", "Fuente de prueba");
        var vista = JsonDocument.Parse(texto).RootElement;
        vista.GetProperty("estado").GetString().Should().Be("Activa");
        var simit = vista.GetProperty("canales").EnumerateArray().Single(c => c.GetProperty("canal").GetString() == "SimitNit");
        simit.GetProperty("descripcion").GetString().Should().Be("Días hábiles a las 07:00, 13:00");
        simit.GetProperty("proximaConsulta").GetDateTime().Should().Be(new DateTime(2026, 10, 13, 13, 0, 0), "son las 09:00 del martes 13");

        var admin = await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/admin/empresas/{empresa}/consultas", Ct);
        admin.GetProperty("productoHabilitado").GetBoolean().Should().BeTrue();
        admin.GetProperty("proveedores").EnumerateArray().Select(p => p.GetProperty("clave").GetString()).Should().Contain(["prueba", "sin_contrato"]);
        admin.GetProperty("canales").EnumerateArray().Single(c => c.GetProperty("canal").GetString() == "SimitNit").GetProperty("proveedor").GetString().Should().Be("prueba");
        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.ConfiguracionVer])).GetAsync($"/api/v1/comparendos/admin/empresas/{empresa}/consultas", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("SimitNit", "{\"encendido\":true,\"proveedor\":\"sin_contrato\",\"modo\":\"HorasFijas\",\"horas\":[\"07:00\"]}", "PROVEEDOR_NO_CONECTADO")]
    [InlineData("MunicipalNit", "{\"encendido\":true,\"proveedor\":\"otra\",\"modo\":\"HorasFijas\",\"horas\":[\"07:00\"]}", "PROVEEDOR_NO_ATIENDE")]
    [InlineData("SimitNit", "{\"encendido\":true,\"proveedor\":\"inventado\",\"modo\":\"HorasFijas\",\"horas\":[\"07:00\"]}", "PROVEEDOR_INVALIDO")]
    [InlineData("SimitNit", "{\"encendido\":true,\"proveedor\":\"prueba\",\"modo\":\"Intervalo\",\"intervaloMinutos\":10,\"desde\":\"06:00\",\"hasta\":\"20:00\"}", "AGENDA_INVALIDA")]
    [InlineData("SimitNit", "{\"encendido\":true,\"proveedor\":\"prueba\",\"modo\":\"HorasFijas\",\"horas\":[\"25:00\"]}", "HORA_INVALIDA")]
    public async Task Validaciones(string canal, string cuerpo, string codigo)
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, _) = await EmpresaConNitAsync();
        var r = await Super.PutAsync($"/api/v1/comparendos/admin/empresas/{empresa}/consultas/canales/{canal}", new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/json"), Ct);
        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadAsStringAsync(Ct)).Should().Contain(codigo);
        var activar = await Super.PostAsync($"/api/v1/comparendos/admin/empresas/{empresa}/consultas/activar", null, Ct);
        (await activar.Content.ReadAsStringAsync(Ct)).Should().Contain("SIN_CANALES");
    }
}

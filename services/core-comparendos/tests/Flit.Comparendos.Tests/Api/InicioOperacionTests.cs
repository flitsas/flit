using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// Rediseño de Comparendos R2: la lente de operación del Inicio de la gestora (<c>/comparendos/operacion</c>, con
/// BandejaVer y sin exigir reportes) y la salud de la plataforma del SuperAdmin (<c>/admin/resumen</c>).
/// </summary>
public sealed class InicioOperacionTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    /// <summary>
    /// Empresa con cinco comparendos el 9-oct-2026: tres de la misma placa (uno de SOAT y uno de técnico-mecánica), uno
    /// de otra placa notificado en nov-2025 (caduca en nov-2026) y uno pagado (no cuenta como pendiente).
    /// </summary>
    private async Task<(Guid Empresa, Dictionary<string, Guid> Ids)> EscenarioAsync(bool reportes, CancellationToken ct)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api, reportes: reportes);
        var ids = await IngresarAsync(api, empresa,
            Item("O-1", placa: "AAA111") with { CodigoInfraccion = "D02" },
            Item("O-2", placa: "AAA111") with { CodigoInfraccion = "C35" },
            Item("O-3", placa: "AAA111"),
            Item("O-4", placa: "BBB222", notificacion: new DateOnly(2025, 11, 14)),
            Item("O-5", placa: "CCC333", valor: 1000000m));
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == ids["O5"]).ExecuteUpdateAsync(u => u.SetProperty(c => c.Pagado, true), ct));
        return (empresa, ids);
    }

    [Fact]
    public async Task Gestora_SinReportes_VeLaOperacion_SinPesos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, _) = await EscenarioAsync(reportes: false, ct);
        var gestora = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer]));

        var r = await gestora.GetAsync("/api/v1/comparendos/comparendos/operacion", ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, "es operación: pide BandejaVer, no el módulo de reportes");
        var o = (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("operacion");
        o.GetProperty("conValores").GetBoolean().Should().BeFalse();
        var resumen = o.GetProperty("resumen");
        (resumen.GetProperty("pendientes").GetInt32(), resumen.GetProperty("vehiculos").GetInt32()).Should().Be((4, 2), "O-5 está pagado");
        resumen.GetProperty("valorPendiente").ValueKind.Should().Be(JsonValueKind.Null, "sin el módulo de reportes no hay pesos");

        var vehiculos = o.GetProperty("vehiculos");
        vehiculos.GetProperty("conTresOMas").GetInt32().Should().Be(1);
        var primero = vehiculos.GetProperty("top")[0];
        (primero.GetProperty("placa").GetString(), primero.GetProperty("cantidad").GetInt32()).Should().Be(("AAA111", 3));
        primero.GetProperty("valor").ValueKind.Should().Be(JsonValueKind.Null);

        var evitables = o.GetProperty("evitables");
        (evitables.GetProperty("soat").GetProperty("cantidad").GetInt32(), evitables.GetProperty("tecnicoMecanica").GetProperty("cantidad").GetInt32()).Should().Be((1, 1));
        evitables.GetProperty("soat").GetProperty("valor").ValueKind.Should().Be(JsonValueKind.Null);

        // Calendario: O-4 (notificado el 14-nov-2025) caduca al año, en nov-2026; nada prescribe en la ventana.
        var calendario = o.GetProperty("calendario").EnumerateArray().ToList();
        calendario.Should().HaveCount(6);
        calendario[0].GetProperty("periodo").GetString().Should().Be("2026-10");
        calendario.Single(m => m.GetProperty("periodo").GetString() == "2026-11").GetProperty("caducan").GetInt32().Should().Be(1);
        calendario.Sum(m => m.GetProperty("prescriben").GetInt32()).Should().Be(0);

        // Antigüedad: todos se impusieron el 28-sep-2026, a menos de 30 días.
        o.GetProperty("antiguedad")[0].GetProperty("cantidad").GetInt32().Should().Be(4);

        // El embudo da la cifra que muestra la bandeja con el mismo estadoProceso.
        foreach (var paso in o.GetProperty("gestion").GetProperty("pasos").EnumerateArray())
        {
            var estado = paso.GetProperty("estadoProceso").GetString();
            var bandeja = await gestora.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos?estadoProceso={estado}", ct);
            paso.GetProperty("cantidad").GetInt32().Should().Be(bandeja.GetProperty("total").GetInt32(), estado);
        }

        o.GetProperty("resumen").GetProperty("nuevos30").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task Gestora_ConReportes_VeLosPesos_YNadaDeOtraEmpresa()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, _) = await EscenarioAsync(reportes: true, ct);
        var gestora = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer, Permisos.ReportesVer]));

        var o = (await gestora.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos/operacion", ct)).GetProperty("operacion");
        o.GetProperty("conValores").GetBoolean().Should().BeTrue();
        o.GetProperty("resumen").GetProperty("valorPendiente").GetDecimal().Should().Be(4 * 604100m);
        o.GetProperty("vehiculos").GetProperty("top")[0].GetProperty("valor").GetDecimal().Should().Be(3 * 604100m);

        // El embudo coincide con la bandeja también con reportes, y para el SuperAdmin que elige la empresa.
        var super_ = (await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/operacion?empresa={empresa}", ct)).GetProperty("operacion");
        foreach (var (cliente, op, sufijo) in new[] { (gestora, o, ""), (Super, super_, $"&empresa={empresa}") })
        {
            foreach (var paso in op.GetProperty("gestion").GetProperty("pasos").EnumerateArray())
            {
                var estado = paso.GetProperty("estadoProceso").GetString();
                var bandeja = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos?estadoProceso={estado}{sufijo}", ct);
                paso.GetProperty("cantidad").GetInt32().Should().Be(bandeja.GetProperty("total").GetInt32(), $"{estado}{sufijo}");
            }
        }

        // Con el módulo pero sin el permiso ReportesVer, tampoco hay pesos.
        var soloBandeja = (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer]))
            .GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos/operacion", ct)).GetProperty("operacion");
        soloBandeja.GetProperty("conValores").GetBoolean().Should().BeFalse();

        // Aislamiento: otra empresa no aparece, pedir otra empresa da 403 y sin BandejaVer también.
        var otra = await EmpresaAsync(api, reportes: true);
        var ajena = (await api.Cliente(new UsuarioDePrueba(otra, [Permisos.BandejaVer])).GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos/operacion", ct))
            .GetProperty("operacion");
        (ajena.GetProperty("resumen").GetProperty("pendientes").GetInt32(), ajena.GetProperty("vehiculos").GetProperty("top").GetArrayLength()).Should().Be((0, 0));
        (await gestora.GetAsync($"/api/v1/comparendos/comparendos/operacion?empresa={otra}", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.ReportesVer])).GetAsync("/api/v1/comparendos/comparendos/operacion", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // El SuperAdmin elige empresa (sin ella, 400) y ve los pesos.
        (await Super.GetAsync("/api/v1/comparendos/comparendos/operacion", ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/operacion?empresa={empresa}", ct)).GetProperty("operacion")
            .GetProperty("resumen").GetProperty("pendientes").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task SuperAdmin_Resumen_SaludYEmpresasQueNecesitanAlgo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        // Una empresa con DP y sin NIT monitoreado, sin representante ni correo para respuestas.
        var empresa = await EmpresaAsync(api, dp: true);
        await api.EnDbAsync(db => db.ConfiguracionesEmpresa.Where(c => c.TenantId == empresa)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.CorreoRespuesta, (string?)null), ct));

        var r = await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/resumen", ct);
        r.GetProperty("empresas").GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        var mia = r.GetProperty("necesitanAlgo").EnumerateArray().Single(e => e.GetProperty("empresaId").GetGuid() == empresa);
        mia.GetProperty("motivos").EnumerateArray().Select(m => m.GetString()).Should()
            .Contain(["Sin NIT ni placa monitoreados", "Sin representante legal", "Sin correo para respuestas"]);

        // La misma regla que el Parametrizador, que además dice si tiene el correo para respuestas.
        var fila = (await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/empresas", ct)).EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == empresa);
        fila.GetProperty("correoRespuesta").GetBoolean().Should().BeFalse();
        fila.GetProperty("faltantes").EnumerateArray().Select(m => m.GetString()).Should().Contain(["Sin NIT ni placa monitoreados", "Sin representante legal"]);

        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer, Permisos.ReportesVer])).GetAsync("/api/v1/comparendos/admin/resumen", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "solo el SuperAdmin");
    }
}

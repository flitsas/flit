using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Modulos.Reportes;
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
    public async Task Antiguedad_SeisTramos_Resueltos30_YDesempateDelTop()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        // Un pendiente por tramo (el sexto, sin fecha de imposición, como lo que entra por OCR) y tres códigos empatados
        // a dos; A-1 se paga y A-2 se inactiva: dejan de estar pendientes y cuentan como resueltos en 30 días.
        ItemFuente En(string numero, DateOnly? imposicion, string codigo) => Item(numero) with { FechaImposicion = imposicion, CodigoInfraccion = codigo };
        var ids = await IngresarAsync(api, empresa,
            En("A-1", new DateOnly(2026, 9, 28), "D07"),
            En("A-2", new DateOnly(2026, 5, 1), "D07"),
            En("A-7", new DateOnly(2026, 9, 28), "C14"),
            En("A-8", new DateOnly(2026, 5, 1), "C29"),
            En("A-3", new DateOnly(2026, 1, 15), "C02"),
            En("A-4", new DateOnly(2024, 6, 1), "C02"),
            En("A-5", new DateOnly(2023, 1, 1), "C29"),
            En("A-6", null, "C14"));
        using var pago = new MultipartFormDataContent { { new StringContent("BC"), "tipo" }, { new StringContent("604100"), "valor" }, { new StringContent("2026-10-08"), "fecha" } };
        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.PagosRegistrar])).PostAsync($"/api/v1/comparendos/comparendos/{ids["A1"]}/pago", pago, ct)).EnsureSuccessStatusCode();
        var cerrado = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == ids["A2"]).ExecuteUpdateAsync(u => u.SetProperty(c => c.Activo, false).SetProperty(c => c.InactivadoEn, cerrado)
            .SetProperty(c => c.MotivoInactivacion, Flit.Comparendos.Domain.Modelo.MotivoInactivacion.InactivadoOperador), ct));

        var o = (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer])).GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos/operacion", ct)).GetProperty("operacion");
        o.GetProperty("resumen").GetProperty("resueltos30").GetInt32().Should().Be(2, "A-1 pagado y A-2 inactivado en los últimos 30 días");
        o.GetProperty("resumen").GetProperty("pendientes").GetInt32().Should().Be(6);

        string? F(JsonElement e, string campo) => e.GetProperty(campo).ValueKind == JsonValueKind.Null ? null : e.GetProperty(campo).GetString();
        var tramos = o.GetProperty("antiguedad").EnumerateArray().Select(x => (x.GetProperty("tramo").GetString(), x.GetProperty("cantidad").GetInt32(), F(x, "desde"), F(x, "hasta"))).ToList();
        tramos.Should().Equal(
            ("Menos de 30 días", 1, "2026-09-09", null),
            ("De 1 a 6 meses", 1, "2026-04-09", "2026-09-08"),
            ("De 6 a 12 meses", 1, "2025-10-09", "2026-04-08"),
            ("De 1 a 3 años", 1, "2023-10-09", "2025-10-08"),
            ("Más de 3 años", 1, null, "2023-10-08"),
            ("Sin fecha de imposición", 1, null, null));
        tramos.Sum(x => x.Item2).Should().Be(6, "los tramos suman lo pendiente");

        // Empate a dos en C02, C14 y C29: se desempata por código.
        o.GetProperty("infracciones").EnumerateArray().Select(i => (i.GetProperty("codigo").GetString(), i.GetProperty("cantidad").GetInt32()))
            .Should().Equal(("C02", 2), ("C14", 2), ("C29", 2));
    }

    [Fact]
    public void DpPorOrganismo_MasEnviadosPrimero_YEnEmpatePorNombre()
    {
        DpOrganismo F(string nombre, int enviados) => new(Guid.NewGuid(), nombre, enviados, 0, 0, null);
        var filas = Enumerable.Range(1, 7).Select(i => F($"Org {i:00}", 1)).Concat([F("Zipaquirá", 5), F("Bello", 5), F("Cali", 2)]);
        ServicioReportes.OrdenarDpPorOrganismo(filas).Select(o => o.Nombre).Should()
            .Equal("Bello", "Zipaquirá", "Cali", "Org 01", "Org 02", "Org 03", "Org 04", "Org 05");
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
        // EmpresaAsync activa las consultas pero no habilita tipologías: con DP, también falta eso (REX: verificar el supuesto).
        mia.GetProperty("motivos").EnumerateArray().Select(m => m.GetString()).Should().BeEquivalentTo(
            ["Sin NIT ni placa monitoreados", "Sin representante legal", "Sin tipologías de DP habilitadas", "Sin correo para respuestas"]);
        // La tarjeta de correos cuenta desde la medianoche de Colombia de hace 7 días, como /mensajes?desde=.
        DateOnly.Parse(r.GetProperty("correosDesde").GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(DateOnly.Parse(r.GetProperty("hoy").GetString()!, System.Globalization.CultureInfo.InvariantCulture).AddDays(-7));

        // La misma regla que el Parametrizador, que además dice si tiene el correo para respuestas.
        var fila = (await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/empresas", ct)).EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == empresa);
        fila.GetProperty("correoRespuesta").GetBoolean().Should().BeFalse();
        fila.GetProperty("faltantes").EnumerateArray().Select(m => m.GetString()).Should().Contain(["Sin NIT ni placa monitoreados", "Sin representante legal"]);

        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer, Permisos.ReportesVer])).GetAsync("/api/v1/comparendos/admin/resumen", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "solo el SuperAdmin");
    }
}

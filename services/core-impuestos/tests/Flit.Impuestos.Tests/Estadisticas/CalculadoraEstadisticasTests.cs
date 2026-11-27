using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Reportes;
using FluentAssertions;
using Xunit;
using E = Flit.Impuestos.Tests.Reportes.EntornoReportes;

namespace Flit.Impuestos.Tests.Estadisticas;

/// <summary>
/// HU #14694 (M14; F4a-CF1..CF4; esquema <c>Dashboard</c> de impuestos.v1.yaml 1.1.0, deltas 10 y 13) — <c>GET /dashboard</c> de una
/// compañía: el monto suma <c>totalAPagar</c> sin los null y es null (nunca 0) si nadie trae monto; <c>liquidacionesSinMonto</c> y
/// <c>parcial</c>; «pendiente de webhook» (enviada con <c>callbackUrl</c>) frente a «por consulta»; contadores de la propia
/// compañía; serie por día con <c>individuales</c>/<c>masivas</c>; <c>porDepartamento[].monto</c> nullable; rango por defecto de
/// 30 días y el 400 con el literal del contrato.
/// </summary>
/// <example>
/// <code>
/// var d = await EntornoReportes.GetJsonAsync(cliente, "/api/v1/impuestos/dashboard?desde=2026-11-01&amp;hasta=2026-11-25", ct);
/// </code>
/// </example>
public sealed class CalculadoraEstadisticasTests(ApiReportesFixture fixture) : IClassFixture<ApiReportesFixture>
{
    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public async Task El_monto_excluye_los_null_cuenta_las_liquidaciones_sin_monto_y_marca_parcial()
    {
        var ct = Preparar();
        var empresa = await EmpresaAsync(ct);
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, null,
            new ItemSemilla("AAA001", ResultadoStatus: "con_deuda", Total: 100m),
            new ItemSemilla("AAA002", ResultadoStatus: "con_deuda", Total: 250.5m),
            new ItemSemilla("AAA003", ResultadoStatus: "con_deuda", Total: null),
            new ItemSemilla("AAA004", ResultadoStatus: "paz_y_salvo", Total: 0m));

        var d = await DashboardAsync(empresa, string.Empty, ct);

        d.GetProperty("montoTotal").GetDecimal().Should().Be(350.5m, "la suma no cuenta el null como 0");
        d.GetProperty("liquidacionesSinMonto").GetInt32().Should().Be(1);
        d.GetProperty("parcial").GetBoolean().Should().BeTrue("hay una liquidación sin monto");
        d.GetProperty("deuda").GetProperty("placasConDeuda").GetInt32().Should().Be(3);
        d.GetProperty("deuda").GetProperty("placasAlDia").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Sin_ningun_monto_el_total_es_null_y_nunca_0()
    {
        var ct = Preparar();
        var empresa = await EmpresaAsync(ct);
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, null,
            new ItemSemilla("BBB001", ResultadoStatus: "con_deuda", Total: null, Departamento: "25", DepartamentoNombre: "Cundinamarca"),
            new ItemSemilla("BBB002", EstadoItem.Enviado, ConCallback: true, Departamento: "25", DepartamentoNombre: "Cundinamarca"));

        var d = await DashboardAsync(empresa, string.Empty, ct);

        d.GetProperty("montoTotal").ValueKind.Should().Be(JsonValueKind.Null, "null = «Sin dato» (delta 13)");
        d.GetProperty("liquidacionesSinMonto").GetInt32().Should().Be(1);
        d.GetProperty("parcial").GetBoolean().Should().BeTrue();
        var depto = d.GetProperty("porDepartamento").EnumerateArray().Should().ContainSingle().Subject;
        depto.GetProperty("codigo").GetString().Should().Be("25");
        depto.GetProperty("nombre").GetString().Should().Be("Cundinamarca");
        depto.GetProperty("total").GetInt32().Should().Be(2);
        depto.GetProperty("monto").ValueKind.Should().Be(JsonValueKind.Null, "monto del departamento sin dato (delta 10)");
    }

    [Fact]
    public async Task Una_consulta_partial_marca_parcial_aunque_todas_traigan_monto()
    {
        var ct = Preparar();
        var empresa = await EmpresaAsync(ct);
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, null,
            new ItemSemilla("CCC001", ResultadoStatus: "con_deuda", Total: 10m, Parcial: true));

        var d = await DashboardAsync(empresa, string.Empty, ct);

        d.GetProperty("montoTotal").GetDecimal().Should().Be(10m);
        d.GetProperty("liquidacionesSinMonto").GetInt32().Should().Be(0);
        d.GetProperty("parcial").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Pendientes_de_webhook_se_separan_de_los_pendientes_por_consulta()
    {
        var ct = Preparar();
        var empresa = await EmpresaAsync(ct);
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, null,
            new ItemSemilla("DDD001", EstadoItem.Enviado, ConCallback: true),
            new ItemSemilla("DDD002", EstadoItem.Enviado, ConCallback: true),
            new ItemSemilla("DDD003", EstadoItem.Enviado, ConCallback: false),
            new ItemSemilla("DDD004", EstadoItem.Pendiente),
            new ItemSemilla("DDD005", EstadoItem.Incierto),
            new ItemSemilla("DDD006", EstadoItem.InciertoRequiereDecision),
            new ItemSemilla("DDD007", EstadoItem.DatosIncompletos, CampoFaltante: "vehicleModel"),
            new ItemSemilla("DDD008", EstadoItem.DepartamentoNoHabilitado),
            new ItemSemilla("DDD009", EstadoItem.Error, ErrorCodigo: "portal_unavailable"),
            new ItemSemilla("DDD010", EstadoItem.SinResultadoLoteCerrado),
            new ItemSemilla("DDD011", ResultadoStatus: "paz_y_salvo", Total: 0m));

        var r = (await DashboardAsync(empresa, string.Empty, ct)).GetProperty("resultados");

        r.GetProperty("pendientesWebhook").GetInt32().Should().Be(2);
        r.GetProperty("pendientesConsulta").GetInt32().Should().Be(2, "la enviada sin callbackUrl y la que aún no sale");
        r.GetProperty("inciertas").GetInt32().Should().Be(2);
        r.GetProperty("datosIncompletos").GetInt32().Should().Be(1);
        r.GetProperty("departamentoNoHabilitado").GetInt32().Should().Be(1);
        r.GetProperty("conError").GetInt32().Should().Be(1);
        r.GetProperty("sinResultado").GetInt32().Should().Be(1);
        r.GetProperty("exitosas").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task NO_APLICA_cuenta_aparte_ni_como_error_ni_como_exitosa_ni_como_pendiente()
    {
        var ct = Preparar();
        var empresa = await EmpresaAsync(ct);
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, null,
            new ItemSemilla("NAP001", EstadoItem.NoAplica, ErrorCodigo: "servicio_publico"),
            new ItemSemilla("NAP002", EstadoItem.NoAplica, ErrorCodigo: "servicio_publico"),
            new ItemSemilla("NAP003", EstadoItem.Error, ErrorCodigo: "portal_unavailable"),
            new ItemSemilla("NAP004", ResultadoStatus: "paz_y_salvo", Total: 0m));

        var r = (await DashboardAsync(empresa, string.Empty, ct)).GetProperty("resultados");

        r.GetProperty("noAplica").GetInt32().Should().Be(2);
        r.GetProperty("conError").GetInt32().Should().Be(1, "NO_APLICA no es un error");
        r.GetProperty("exitosas").GetInt32().Should().Be(1);
        r.GetProperty("pendientesConsulta").GetInt32().Should().Be(0, "NO_APLICA es final");
        r.GetProperty("pendientesWebhook").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Los_contadores_la_serie_y_los_ultimos_lotes_son_solo_de_la_propia_compania()
    {
        var ct = Preparar();
        var empresa = await EmpresaAsync(ct);
        var otra = await EmpresaAsync(ct);
        var ayer = DateTimeOffset.UtcNow.AddDays(-1);
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, ayer, new ItemSemilla("EEE001", ResultadoStatus: "paz_y_salvo", Total: 0m));
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, null,
            new ItemSemilla("EEE002", EstadoItem.Pendiente), new ItemSemilla("EEE003", EstadoItem.Pendiente), new ItemSemilla("EEE004", ResultadoStatus: "con_deuda", Total: 5m));
        await E.SembrarAsync(Api, otra, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, null,
            new ItemSemilla("ZZZ001", ResultadoStatus: "con_deuda", Total: 999m), new ItemSemilla("ZZZ002", ResultadoStatus: "con_deuda", Total: 999m));

        var d = await DashboardAsync(empresa, string.Empty, ct);

        var c = d.GetProperty("contadores");
        c.GetProperty("total").GetInt32().Should().Be(4);
        c.GetProperty("individuales").GetInt32().Should().Be(1);
        c.GetProperty("masivas").GetInt32().Should().Be(3);
        c.GetProperty("lotesEnProceso").GetInt32().Should().Be(1);
        d.GetProperty("montoTotal").GetDecimal().Should().Be(5m, "lo de la otra compañía no suma");
        d.GetProperty("ranking").ValueKind.Should().Be(JsonValueKind.Null, "el ranking es solo del Super Admin en la vista global");

        var serie = d.GetProperty("serie").EnumerateArray().ToList();
        serie.Should().HaveCount(30, "un punto por día del rango por defecto");
        serie.Sum(p => p.GetProperty("total").GetInt32()).Should().Be(4);
        foreach (var p in serie)
            p.GetProperty("total").GetInt32().Should().Be(p.GetProperty("individuales").GetInt32() + p.GetProperty("masivas").GetInt32());
        var hoy = serie.Single(p => p.GetProperty("fecha").GetString() == E.Hoy());
        hoy.GetProperty("masivas").GetInt32().Should().Be(3);
        hoy.GetProperty("individuales").GetInt32().Should().Be(0);

        var lotes = d.GetProperty("ultimosLotes").EnumerateArray().ToList();
        lotes.Should().HaveCount(2);
        lotes[0].GetProperty("tipo").GetString().Should().Be("MASIVA", "el más reciente primero");
        lotes[1].GetProperty("placa").GetString().Should().Be("EEE001");
        lotes.Should().OnlyContain(l => l.GetProperty("empresa").GetProperty("id").GetGuid() == empresa);
    }

    [Fact]
    public async Task Los_ultimos_lotes_marcan_placaNoInscrita_solo_en_la_individual()
    {
        var ct = Preparar();
        var empresa = await EmpresaAsync(ct);
        var ahora = DateTimeOffset.UtcNow;
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, ahora.AddMinutes(-3),
            new ItemSemilla("NOI001", EstadoItem.Error, ErrorCodigo: ClasificacionErrores.CodigoPortalRechazo,
                ErrorMensaje: "La placa ingresada no se encuentra inscrita en nuestra base de datos."));
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, ahora.AddMinutes(-2),
            new ItemSemilla("OTR001", EstadoItem.Error, ErrorCodigo: "portal_unavailable", ErrorMensaje: "Mantenimiento del portal."));
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, ahora.AddMinutes(-1), new ItemSemilla("MAS001"), new ItemSemilla("MAS002"));

        var d = await DashboardAsync(empresa, string.Empty, ct);

        var lotes = d.GetProperty("ultimosLotes").EnumerateArray().ToList();
        lotes.Should().HaveCount(3);
        lotes[0].GetProperty("placaNoInscrita").ValueKind.Should().Be(JsonValueKind.Null, "la masiva lleva null");
        lotes[1].GetProperty("placaNoInscrita").GetBoolean().Should().BeFalse("OTR001: otro error");
        lotes[2].GetProperty("placaNoInscrita").GetBoolean().Should().BeTrue("NOI001: placa no inscrita");
    }

    [Fact]
    public async Task El_rango_por_defecto_es_de_30_dias_y_lo_de_antes_no_cuenta()
    {
        var ct = Preparar();
        var empresa = await EmpresaAsync(ct);
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, DateTimeOffset.UtcNow.AddDays(-40),
            new ItemSemilla("FFF001", ResultadoStatus: "con_deuda", Total: 7m));

        var d = await DashboardAsync(empresa, string.Empty, ct);
        var conRango = await DashboardAsync(empresa, $"?desde={E.Hoy(45)}&hasta={E.Hoy()}", ct);

        d.GetProperty("periodo").GetProperty("desde").GetString().Should().Be(E.Hoy(29));
        d.GetProperty("periodo").GetProperty("hasta").GetString().Should().Be(E.Hoy());
        d.GetProperty("contadores").GetProperty("total").GetInt32().Should().Be(0);
        d.GetProperty("montoTotal").ValueKind.Should().Be(JsonValueKind.Null);
        d.GetProperty("ultimosLotes").GetArrayLength().Should().Be(0);
        conRango.GetProperty("contadores").GetProperty("total").GetInt32().Should().Be(1);
        conRango.GetProperty("serie").GetArrayLength().Should().Be(46);
    }

    [Fact]
    public async Task Un_rango_invalido_responde_400_con_el_literal_del_contrato()
    {
        var ct = Preparar();
        var empresa = await EmpresaAsync(ct);
        var cliente = Api.Cliente(Api.Token(empresa: empresa, permisos: E.Ver));

        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync($"{E.Dashboard}?desde={E.Hoy()}&hasta={E.Hoy(3)}", ct),
            HttpStatusCode.BadRequest, "RANGO_INVALIDO", "El rango de fechas no es válido.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync($"{E.Dashboard}?desde={E.Hoy(400)}&hasta={E.Hoy()}", ct),
            HttpStatusCode.BadRequest, "RANGO_INVALIDO", "El rango de fechas no es válido.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync($"{E.Dashboard}?desde=25-11-2026", ct),
            HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "Un filtro no es válido.");
    }

    [Fact]
    public async Task Sin_el_permiso_del_dashboard_responde_403()
    {
        var ct = Preparar();
        var empresa = await EmpresaAsync(ct);

        using var r = await Api.Cliente(Api.Token(empresa: empresa, permisos: [Permisos.LiquidacionesVer])).GetAsync(E.Dashboard, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private Task<JsonElement> DashboardAsync(Guid empresa, string consulta, CancellationToken ct) =>
        E.GetJsonAsync(Api.Cliente(Api.Token(empresa: empresa, permisos: E.Ver)), E.Dashboard + consulta, ct);

    private async Task<Guid> EmpresaAsync(CancellationToken ct)
    {
        var empresa = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, empresa, ct);
        return empresa;
    }

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        return TestContext.Current.CancellationToken;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Ejecucion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;

namespace Flit.Impuestos.Tests.Liquidaciones;

/// <summary>
/// HU #14694 (M12; F3-CF12, DA-6b, delta 5 de impuestos.v1.yaml 1.1.0) — <c>POST /liquidaciones/{id}/cerrar</c>: solo el
/// Administrador Impuestos (<c>impuestos.liquidaciones.cerrar</c>) o el Super Admin (con <c>?empresa</c>); el Gestor recibe 403. Los
/// ítems no finales pasan a <c>SIN_RESULTADO_LOTE_CERRADO</c>, los finales no cambian, y el lote queda <c>CERRADO_MANUALMENTE</c>
/// con <c>cerradaPor</c> y <c>cerradaEn</c>. Un lote terminado responde 409 <c>YA_TERMINADA</c>. El motivo admite hasta 300
/// caracteres y queda en <c>motivo_cierre</c> (<c>motivoCierre</c>); <c>cerradaPor.nombre</c> sale de <c>cerrada_por_nombre</c> (el
/// nombre del token; sin claim <c>name</c>, el correo). Cerrar libera el crédito de los ítems en vuelo.
/// </summary>
/// <example>
/// <code>
/// var r = await admin.PostAsJsonAsync($"/api/v1/impuestos/liquidaciones/{id}/cerrar", new { motivo = "Lote de prueba" }, ct);
/// </code>
/// </example>
public sealed class CerrarLiquidacionTests
{
    private static readonly string[] Admin = [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear, Permisos.LiquidacionesCerrar];
    private static readonly string[] Gestor = [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear];

    [Fact]
    public async Task El_Admin_cierra_los_pendientes_pasan_a_sin_resultado_y_los_finales_no_cambian()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, items) = await EvaluadorTerminacionTests.SembrarMasivaAsync(
            api, ct, EstadoItem.Exitoso, EstadoItem.Enviado, EstadoItem.Pendiente, EstadoItem.InciertoRequiereDecision, EstadoItem.ReintentoProgramado);
        var admin = Guid.NewGuid();

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Admin, usuario: admin, roles: ["admin_impuestos"]))
            .PostAsJsonAsync(Ruta(lote), new { motivo = "Proveedor caído" }, ct);

        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        using var cuerpo = JsonDocument.Parse(texto);
        var raiz = cuerpo.RootElement;
        raiz.GetProperty("estado").GetString().Should().Be("CERRADO_MANUALMENTE");
        raiz.GetProperty("cerradaPor").GetProperty("id").GetGuid().Should().Be(admin);
        raiz.GetProperty("cerradaPor").GetProperty("nombre").GetString().Should().Be("operador@empresa.test", "el token de prueba no trae name: el correo");
        raiz.GetProperty("cerradaEn").ValueKind.Should().Be(JsonValueKind.String);
        raiz.GetProperty("motivoCierre").GetString().Should().Be("Proveedor caído", "columna motivo_cierre (1.2.0)");
        raiz.GetProperty("contadores").GetProperty("sinResultado").GetInt32().Should().Be(3);
        raiz.GetProperty("contadores").GetProperty("pendientes").GetInt32().Should().Be(0);
        raiz.GetProperty("contadores").GetProperty("incierto").GetInt32().Should().Be(1);

        var estados = (await M.ItemsAsync(api, empresa, lote, ct)).ToDictionary(i => i.Id, i => i.Estado);
        estados[items[0]].Should().Be(EstadoItem.Exitoso);
        estados[items[1]].Should().Be(EstadoItem.SinResultadoLoteCerrado);
        estados[items[2]].Should().Be(EstadoItem.SinResultadoLoteCerrado);
        estados[items[3]].Should().Be(EstadoItem.InciertoRequiereDecision);
        estados[items[4]].Should().Be(EstadoItem.SinResultadoLoteCerrado);
        var cabecera = await EntornoEjecucion.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.CerradoManualmente);
        cabecera.CerradaPor.Should().Be(admin);
        cabecera.CerradaEn.Should().NotBeNull();
    }

    [Fact]
    public async Task El_Gestor_recibe_403_y_el_lote_no_cambia()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, _) = await EvaluadorTerminacionTests.SembrarMasivaAsync(api, ct, EstadoItem.Enviado, EstadoItem.Pendiente);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync(Ruta(lote), new { }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).Estado.Should().Be(EstadoLote.EnProceso);
    }

    [Fact]
    public async Task El_Super_Admin_cierra_con_empresa_y_sin_ella_recibe_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, _) = await EvaluadorTerminacionTests.SembrarMasivaAsync(api, ct, EstadoItem.Enviado, EstadoItem.Pendiente);
        var superAdmin = Guid.NewGuid();
        var sa = api.Cliente(api.Token(superAdmin: true, tipoEmpresa: "FLIT", usuario: superAdmin));

        await LiquidacionIndividualEndpointsTests.EsperarError(await sa.PostAsync(Ruta(lote), null, ct), HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.");
        var r = await sa.PostAsync($"{Ruta(lote)}?empresa={empresa}", null, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        var cabecera = await EntornoEjecucion.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.CerradoManualmente);
        cabecera.CerradaPor.Should().Be(superAdmin);
    }

    [Fact]
    public async Task Un_lote_terminado_o_ya_cerrado_responde_409_YA_TERMINADA()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, _) = await EvaluadorTerminacionTests.SembrarMasivaAsync(api, ct, EstadoItem.Enviado, EstadoItem.Pendiente);
        var admin = api.Cliente(api.Token(empresa: empresa, permisos: Admin));

        (await admin.PostAsync(Ruta(lote), null, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        await LiquidacionIndividualEndpointsTests.EsperarError(
            await admin.PostAsync(Ruta(lote), null, ct), HttpStatusCode.Conflict, "YA_TERMINADA", "La liquidación ya terminó: no se puede cerrar.");

        var (completado, _) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["CCC111", "CCC222"], ct, EstadoItem.Exitoso);
        await using (var db = api.Contexto(empresa))
        {
            await db.Set<Liquidacion>().Where(l => l.Id == completado).ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, EstadoLote.Completado), ct);
        }

        await LiquidacionIndividualEndpointsTests.EsperarError(
            await admin.PostAsync(Ruta(completado), null, ct), HttpStatusCode.Conflict, "YA_TERMINADA", "La liquidación ya terminó: no se puede cerrar.");
    }

    [Fact]
    public async Task Un_motivo_de_mas_de_300_caracteres_responde_422_en_el_campo_motivo_y_uno_de_300_cierra()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, _) = await EvaluadorTerminacionTests.SembrarMasivaAsync(api, ct, EstadoItem.Enviado, EstadoItem.Pendiente);
        var admin = api.Cliente(api.Token(empresa: empresa, permisos: Admin));

        var r = await admin.PostAsJsonAsync(Ruta(lote), new { motivo = new string('m', 301) }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using (var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct)))
            cuerpo.RootElement.GetProperty("campo").GetString().Should().Be("motivo");
        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).Estado.Should().Be(EstadoLote.EnProceso);
        (await admin.PostAsJsonAsync(Ruta(lote), new { motivo = new string('m', 300) }, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Una_liquidacion_de_otra_compania_responde_404_LIQUIDACION_NO_ENCONTRADA()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, lote, _) = await EvaluadorTerminacionTests.SembrarMasivaAsync(api, ct, EstadoItem.Enviado, EstadoItem.Pendiente);
        var otra = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, otra, ct);

        var r = await api.Cliente(api.Token(empresa: otra, permisos: Admin)).PostAsync(Ruta(lote), null, ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(r, HttpStatusCode.NotFound, "LIQUIDACION_NO_ENCONTRADA", "La liquidación no existe.");
    }

    [Fact]
    public async Task Cerrar_libera_el_credito_de_los_items_en_vuelo_para_el_siguiente_lote()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, _) = await EvaluadorTerminacionTests.SembrarMasivaAsync(api, ct, EstadoItem.ConsultandoRunt, EstadoItem.ConsultandoRunt);
        var (siguiente, pendientes) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["BBB111", "BBB222"], ct, creadaEn: DateTimeOffset.UtcNow.AddMinutes(1));
        (await EntornoEjecucion.DespacharAsync(api, empresa, ct)).Should().Be(0, "el crédito de la compañía (2) está ocupado");

        (await api.Cliente(api.Token(empresa: empresa, permisos: Admin)).PostAsync(Ruta(lote), null, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await M.ItemsAsync(api, empresa, siguiente, ct)).Should().OnlyContain(i => i.Estado == EstadoItem.ConsultandoRunt);
        pendientes.Should().HaveCount(2);
    }

    private static string Ruta(Guid lote) => $"{M.Liquidaciones}/{lote}/cerrar";
}

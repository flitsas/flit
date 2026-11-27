using System.Net;
using Flit.Impuestos.Api.Modulos.Conciliacion;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Resultados;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Conciliacion;

/// <summary>
/// HU #14694 (M10; F2a-CF7, plan §1.5 DA-4) — el «Incierto»: sin secreto (la consulta salió sin <c>callbackUrl</c>) pasa directo a
/// <c>INCIERTO_REQUIERE_DECISION</c>; con secreto espera 30 min y recorre <c>GET /api/v1/consultas?limit=50&amp;cursor=</c> hasta
/// 10 páginas buscando SU token en <c>callback_url</c>. Si aparece, adopta el id y queda <c>ENVIADO</c>; si no, requiere decisión.
/// Nunca empareja por placa y hora. Una falla del proveedor deja el ítem como estaba y reintenta más tarde.
/// </summary>
/// <example>
/// <code>
/// await api.App.Services.GetRequiredService&lt;TrabajadorConciliacion&gt;().EjecutarRondaAsync(ct);
/// </code>
/// </example>
public sealed class CorrelacionInciertoTests
{
    private const string UrlBase = "https://dev.flit.example/api/v1/impuestos/avisos/";

    [Fact]
    public async Task Sin_secreto_pasa_directo_a_requiere_decision_sin_llamar_al_proveedor_y_el_lote_termina()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var proveedor = new ApiLiquidacionDePrueba();
        await using var api = await R.ConApiAsync(proveedor, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.ListoSinWebhook, null, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var item = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Incierto, ct, i => (i.ConCallback, i.EnviadoEn) = (false, reloj.Ahora));

        await RondaAsync(api, ct);

        (await R.ItemAsync(api, empresa, item.Id, ct)).Estado.Should().Be(EstadoItem.InciertoRequiereDecision);
        proveedor.Pedidos.Should().BeEmpty();
        var cabecera = await R.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.CompletadoConNovedades, "INCIERTO_REQUIERE_DECISION es final para el cierre (DA-6b)");
        cabecera.CIncierto.Should().Be(1);
    }

    [Fact]
    public async Task Con_secreto_espera_30_minutos_antes_de_buscar()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var proveedor = new ApiLiquidacionDePrueba();
        await using var api = await R.ConApiAsync(proveedor, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item, _) = await SembrarInciertoAsync(api, reloj.Ahora.AddMinutes(-29), ct);

        await RondaAsync(api, ct);

        (await R.ItemAsync(api, empresa, item, ct)).Estado.Should().Be(EstadoItem.Incierto);
        proveedor.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Con_secreto_adopta_la_consulta_cuyo_callback_url_lleva_su_token_aunque_este_en_la_segunda_pagina()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        string? token = null;
        var enviado = reloj.Ahora.AddMinutes(-31);
        var proveedor = new ApiLiquidacionDePrueba
        {
            Responder = r => r.RequestUri!.Query.Contains("cursor=p2", StringComparison.Ordinal)
                ? Listado(null, Item("c-mia", UrlBase + token, enviado))
                : Listado("p2", Item("c-otra", UrlBase + R.NuevoToken(), enviado)),
        };
        await using var api = await R.ConApiAsync(proveedor, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item, t) = await SembrarInciertoAsync(api, enviado, ct);
        token = t;

        await RondaAsync(api, ct);

        var leido = await R.ItemAsync(api, empresa, item, ct);
        leido.Estado.Should().Be(EstadoItem.Enviado);
        leido.ConsultaApiId.Should().Be("c-mia");
        leido.ProximoPollEn.Should().NotBeNull().And.BeOnOrBefore(reloj.Ahora, "se consulta de inmediato");
        proveedor.Pedidos.Should().HaveCount(2);
        proveedor.Pedidos.Should().OnlyContain(p => p.Uri.Contains("limit=50", StringComparison.Ordinal) && p.Cabeceras["X-API-Key"] == R.ApiKey);
    }

    [Fact]
    public async Task Nunca_empareja_por_placa_y_hora_sin_su_token()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var enviado = reloj.Ahora.AddMinutes(-40);
        var proveedor = new ApiLiquidacionDePrueba
        {
            // Misma placa y misma hora, pero con otro token (o sin callback): no es suya.
            Responder = _ => Listado(null, Item("c-misma-placa", UrlBase + R.NuevoToken(), enviado), Item("c-sin-callback", null, enviado)),
        };
        await using var api = await R.ConApiAsync(proveedor, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item, _) = await SembrarInciertoAsync(api, enviado, ct);

        await RondaAsync(api, ct);

        var leido = await R.ItemAsync(api, empresa, item, ct);
        leido.Estado.Should().Be(EstadoItem.InciertoRequiereDecision);
        leido.ConsultaApiId.Should().BeNull();
    }

    [Fact]
    public async Task Recorre_como_maximo_10_paginas()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var enviado = reloj.Ahora.AddMinutes(-31);
        var proveedor = new ApiLiquidacionDePrueba { Responder = _ => Listado("siguiente", Item("c-x", UrlBase + R.NuevoToken(), enviado)) };
        await using var api = await R.ConApiAsync(proveedor, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item, _) = await SembrarInciertoAsync(api, enviado, ct);

        await RondaAsync(api, ct);

        proveedor.Pedidos.Should().HaveCount(10);
        (await R.ItemAsync(api, empresa, item, ct)).Estado.Should().Be(EstadoItem.InciertoRequiereDecision);
    }

    [Fact]
    public async Task Una_falla_del_proveedor_deja_el_item_incierto_y_reintenta_mas_tarde()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var proveedor = new ApiLiquidacionDePrueba { Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.ServiceUnavailable, "{}") };
        await using var api = await R.ConApiAsync(proveedor, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item, _) = await SembrarInciertoAsync(api, reloj.Ahora.AddMinutes(-31), ct);

        await RondaAsync(api, ct);

        var leido = await R.ItemAsync(api, empresa, item, ct);
        leido.Estado.Should().Be(EstadoItem.Incierto);
        leido.ProximoPollEn.Should().Be(reloj.Ahora.Add(new OpcionesConciliacion().ReintentoIncierto));

        proveedor.Cola.Clear();
        await RondaAsync(api, ct);
        proveedor.Pedidos.Should().BeEmpty("no se reintenta antes de su hora");
    }

    private static async Task<(Guid Empresa, Guid Item, string Token)> SembrarInciertoAsync(ApiDePrueba api, DateTimeOffset enviadoEn, CancellationToken ct)
    {
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Incierto, null, ct, i => i.EnviadoEn = enviadoEn);
        return (empresa, item.Id, token);
    }

    private static Task<int> RondaAsync(ApiDePrueba api, CancellationToken ct) =>
        api.App.Services.GetRequiredService<TrabajadorConciliacion>().EjecutarRondaAsync(ct);

    private static string Item(string id, string? callbackUrl, DateTimeOffset creada)
    {
        var callback = callbackUrl is null ? "null" : $"\"{callbackUrl}\"";
        return $$$"""{"id":"{{{id}}}","status":"succeeded","attempt":1,"error_code":null,"callback_url":{{{callback}}},"plateNumber":"LHQ479","created_at":"{{{creada.UtcDateTime:O}}}"}""";
    }

    private static HttpResponseMessage Listado(string? siguiente, params string[] items)
    {
        var cursor = siguiente is null ? "null" : $"\"{siguiente}\"";
        var lista = string.Join(",", items);
        return ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, $$$"""{"items":[{{{lista}}}],"next_cursor":{{{cursor}}} }""");
    }
}

using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Ejecucion;
using Flit.Impuestos.Tests.Liquidaciones;
using Flit.Impuestos.Tests.Usuarios;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Avisos;

/// <summary>
/// HU #14694 (M12; F3-CF7, plan §5.9) — aviso «lote terminado»: solo para lotes MASIVA y una sola vez por lote, al entrar en un
/// estado terminal (solo o por cierre manual). Un aviso en pantalla <c>LOTE_TERMINADO</c> para la compañía y un trabajo
/// <c>notificaciones.email.send</c> por la outbox por cada correo configurado: plantilla <c>impuestos.lote.terminado</c>, canal
/// <c>flit_smtp</c>, destinatario <c>{email, nombre = email}</c>, sin tema, con el asunto y los párrafos del plan §5.9 (valores
/// escapados como HTML; el enlace solo con <c>Impuestos:Avisos:UrlFrontend</c>). Sin placas, propietarios, documentos ni montos.
/// <c>GET /notificaciones</c> y <c>POST /notificaciones/{id}/leida</c> (por usuario, idempotente).
/// </summary>
/// <example>
/// <code>
/// var correos = await EntornoMasiva.CorreosAsync(api, ct);
/// EntornoMasiva.Datos(correos[0]).GetProperty("asunto").GetString();
/// </code>
/// </example>
public sealed class AvisoLoteTerminadoTests
{
    private const string UrlFrontend = "https://impuestos.flit.prueba/";
    private static readonly string[] Admin = [Permisos.LiquidacionesVer, Permisos.LiquidacionesCerrar];

    [Fact]
    public async Task Al_cerrar_una_masiva_sale_un_correo_por_direccion_con_los_textos_del_plan_y_un_aviso_en_pantalla()
    {
        var ct = TestContext.Current.CancellationToken;
        var directorio = new DirectorioEmpresasDePrueba();
        await using var api = await M.ConApiAsync(urlFrontend: UrlFrontend, directorio: directorio);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote) = await SembrarAsync(api, ["a@empresa.com", "b@empresa.com"], "flota <marzo> & co.csv", ct,
            EstadoItem.Exitoso, EstadoItem.Error, EstadoItem.DatosIncompletos, EstadoItem.InciertoRequiereDecision, EstadoItem.Enviado);
        directorio.Sembrar(empresa, M.NombreEmpresa, "900123456");

        (await api.Cliente(api.Token(empresa: empresa, permisos: Admin)).PostAsync($"{M.Liquidaciones}/{lote}/cerrar", null, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var correos = await M.CorreosAsync(api, ct);
        correos.Should().HaveCount(2);
        correos.Should().OnlyContain(c => c.Exchange == "flit.notificaciones");
        var datos = correos.Select(M.Datos).ToList();
        datos.Select(d => d.GetProperty("destinatario").GetProperty("email").GetString()).Should().Equal("a@empresa.com", "b@empresa.com");
        foreach (var d in datos)
        {
            d.GetProperty("plantilla").GetString().Should().Be("impuestos.lote.terminado");
            d.GetProperty("canal").GetString().Should().Be("flit_smtp");
            d.GetProperty("destinatario").GetProperty("nombre").GetString().Should().Be(d.GetProperty("destinatario").GetProperty("email").GetString());
            d.GetProperty("asunto").GetString().Should().Be("Impuestos: terminó la liquidación masiva «flota <marzo> & co.csv»");
            d.GetProperty("html").GetString().Should().Be(
                "<p>La liquidación masiva «flota &lt;marzo&gt; &amp; co.csv» de Compañía Demo terminó: Cerrado manualmente.</p>" +
                "<p>Placas: 5. Con resultado: 1. Con novedad: 3. Sin resultado: 1.</p>" +
                $"<p>Consulta el detalle en Impuestos: https://impuestos.flit.prueba/liquidaciones/{lote}.</p>");
            d.TryGetProperty("tema", out _).Should().BeFalse("sin tema: el de FLIT por defecto");
        }

        var payloads = string.Join("\n", correos.Select(c => c.Payload));
        payloads.Should().NotContain("AAA000").And.NotContain("PEDRO").And.NotContain("771700");
        var notificacion = (await M.NotificacionesAsync(api, empresa, ct)).Should().ContainSingle().Subject;
        notificacion.Tipo.Should().Be("LOTE_TERMINADO");
        notificacion.LiquidacionId.Should().Be(lote);
        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).AvisoEnviadoEn.Should().NotBeNull();
    }

    [Fact]
    public async Task Sin_UrlFrontend_el_correo_no_lleva_el_parrafo_del_enlace()
    {
        var ct = TestContext.Current.CancellationToken;
        var directorio = new DirectorioEmpresasDePrueba();
        await using var api = await M.ConApiAsync(directorio: directorio);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote) = await SembrarAsync(api, ["a@empresa.com"], "placas.csv", ct, EstadoItem.Enviado, EstadoItem.Exitoso);
        directorio.Sembrar(empresa, M.NombreEmpresa, null);

        await R.AplicarAsync(api, empresa, (await M.ItemsAsync(api, empresa, lote, ct))[0].Id, R.Consulta("c-0"), ct);

        var html = M.Datos((await M.CorreosAsync(api, ct)).Should().ContainSingle().Subject).GetProperty("html").GetString();
        html.Should().Be(
            "<p>La liquidación masiva «placas.csv» de Compañía Demo terminó: Completado.</p>" +
            "<p>Placas: 2. Con resultado: 2. Con novedad: 0. Sin resultado: 0.</p>");
    }

    [Fact]
    public async Task Un_resultado_tardio_despues_de_terminar_no_repite_el_aviso()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync(urlFrontend: UrlFrontend);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote) = await SembrarAsync(api, ["a@empresa.com"], "placas.csv", ct, EstadoItem.Enviado, EstadoItem.Enviado);
        var admin = api.Cliente(api.Token(empresa: empresa, permisos: Admin));
        (await admin.PostAsync($"{M.Liquidaciones}/{lote}/cerrar", null, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var aviso = (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).AvisoEnviadoEn;

        var items = await M.ItemsAsync(api, empresa, lote, ct);
        await R.AplicarAsync(api, empresa, items[0].Id, R.Consulta("c-0"), ct);
        await R.AplicarAsync(api, empresa, items[1].Id, R.Consulta("c-1"), ct);

        (await M.CorreosAsync(api, ct)).Should().ContainSingle("un solo envío por lote");
        (await M.NotificacionesAsync(api, empresa, ct)).Should().ContainSingle();
        var cabecera = await EntornoEjecucion.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.CerradoManualmente, "el resultado tardío no reabre el lote");
        cabecera.CExitosas.Should().Be(2);
        cabecera.AvisoEnviadoEn.Should().Be(aviso);
    }

    [Fact]
    public async Task Una_individual_que_termina_no_manda_correo_ni_aviso()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync(urlFrontend: UrlFrontend);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        await M.SembrarConfiguracionAsync(api, empresa, ct, correos: ["a@empresa.com"]);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["ABC123"], ct, EstadoItem.Enviado);
        await using (var db = api.Contexto(empresa))
        {
            await db.Set<Liquidacion>().Where(l => l.Id == lote).ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, EstadoLote.EnProceso), ct);
            await db.Set<LiquidacionItem>().Where(i => i.Id == items[0]).ExecuteUpdateAsync(s => s.SetProperty(i => i.ConsultaApiId, "c-0"), ct);
        }

        await R.AplicarAsync(api, empresa, items[0], R.Consulta("c-0"), ct);

        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).Estado.Should().Be(EstadoLote.Completado);
        (await M.CorreosAsync(api, ct)).Should().BeEmpty();
        (await M.NotificacionesAsync(api, empresa, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_correos_configurados_solo_queda_el_aviso_en_pantalla()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync(urlFrontend: UrlFrontend);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote) = await SembrarAsync(api, [], "placas.csv", ct, EstadoItem.Enviado, EstadoItem.Pendiente);

        (await api.Cliente(api.Token(empresa: empresa, permisos: Admin)).PostAsync($"{M.Liquidaciones}/{lote}/cerrar", null, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await M.CorreosAsync(api, ct)).Should().BeEmpty();
        (await M.NotificacionesAsync(api, empresa, ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task La_campana_lista_los_avisos_de_la_compania_y_marca_leido_por_usuario()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote) = await SembrarAsync(api, [], "placas.csv", ct, EstadoItem.Enviado, EstadoItem.Pendiente);
        (await api.Cliente(api.Token(empresa: empresa, permisos: Admin)).PostAsync($"{M.Liquidaciones}/{lote}/cerrar", null, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var yo = api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.LiquidacionesVer], usuario: Guid.NewGuid()));
        var otro = api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.LiquidacionesVer], usuario: Guid.NewGuid()));

        var item = (await ListarAsync(yo, ct)).Should().ContainSingle().Subject;
        item.GetProperty("tipo").GetString().Should().Be("LOTE_TERMINADO");
        item.GetProperty("liquidacionId").GetGuid().Should().Be(lote);
        item.GetProperty("creadaEn").ValueKind.Should().Be(JsonValueKind.String);
        item.GetProperty("leida").GetBoolean().Should().BeFalse();
        var id = item.GetProperty("id").GetGuid();

        (await yo.PostAsync($"{M.Notificaciones}/{id}/leida", null, ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await yo.PostAsync($"{M.Notificaciones}/{id}/leida", null, ct)).StatusCode.Should().Be(HttpStatusCode.NoContent, "idempotente");

        (await ListarAsync(yo, ct)).Single().GetProperty("leida").GetBoolean().Should().BeTrue();
        (await ListarAsync(otro, ct)).Single().GetProperty("leida").GetBoolean().Should().BeFalse("leída es por usuario");
        (await M.NotificacionesAsync(api, empresa, ct)).Single().LeidaPor.Should().HaveCount(1);
        (await yo.PostAsync($"{M.Notificaciones}/{Guid.NewGuid()}/leida", null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var ajena = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, ajena, ct);
        var deOtraCompania = api.Cliente(api.Token(empresa: ajena, permisos: [Permisos.LiquidacionesVer]));
        (await ListarAsync(deOtraCompania, ct)).Should().BeEmpty();
        (await deOtraCompania.PostAsync($"{M.Notificaciones}/{id}/leida", null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Compañía con sus correos y un lote MASIVA en proceso con un ítem por estado (consulta <c>c-{n}</c>).</summary>
    private static async Task<(Guid Empresa, Guid Lote)> SembrarAsync(
        ApiDePrueba api, string[] correos, string nombreArchivo, CancellationToken ct, params EstadoItem[] estados)
    {
        var (empresa, lote, _) = await EvaluadorTerminacionTests.SembrarMasivaAsync(api, ct, estados);
        await using var db = api.Contexto(empresa);
        await db.Set<ConfiguracionEmpresa>().ExecuteUpdateAsync(s => s.SetProperty(c => c.CorreosAviso, correos), ct);
        await db.Set<Liquidacion>().Where(l => l.Id == lote).ExecuteUpdateAsync(s => s.SetProperty(l => l.NombreArchivo, nombreArchivo), ct);
        return (empresa, lote);
    }

    private static async Task<List<JsonElement>> ListarAsync(HttpClient cliente, CancellationToken ct)
    {
        var r = await cliente.GetAsync(M.Notificaciones, ct);
        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        using var doc = JsonDocument.Parse(texto);
        return [.. doc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone())];
    }
}

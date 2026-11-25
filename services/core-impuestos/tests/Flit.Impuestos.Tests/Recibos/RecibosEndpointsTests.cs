using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Infraestructura.Archivos;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Ejecucion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;

namespace Flit.Impuestos.Tests.Recibos;

/// <summary>
/// HU #14694 (M11; impuestos.v1.yaml 1.2.0 <c>descargarRecibo</c>; F2b-CF7, F1b-CF6) —
/// <c>GET /liquidaciones/{id}/items/{itemId}/recibos/{reciboId}</c> con <c>impuestos.recibos.descargar</c>: el PDF guardado en la
/// suite (nunca por proxy al proveedor, nunca con <c>X-API-Key</c>) con las cabeceras de descarga segura; un recibo que no está
/// <c>GUARDADO</c> ⇒ 409 <c>RECIBO_NO_DISPONIBLE</c> con su <c>motivo</c>; inexistente, de otro ítem o de otra compañía ⇒ 404 (igual
/// que inexistente). El Super Admin descarga con <c>?empresa</c> (obligatoria), auditado, también con la compañía suspendida.
/// </summary>
/// <example>
/// <code>
/// var pdf = await cliente.GetByteArrayAsync($"/api/v1/impuestos/liquidaciones/{lote}/items/{item}/recibos/{recibo}", ct);
/// </code>
/// </example>
public sealed class RecibosEndpointsTests
{
    private static readonly string[] Descargar = [Permisos.RecibosDescargar];
    private static readonly byte[] Pdf = ArchivosDelProveedor.Pdf("f1");

    [Fact]
    public async Task El_gestor_descarga_el_pdf_guardado_con_las_cabeceras_de_descarga_segura_y_sin_llamar_al_proveedor()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba();
        await using var api = await M.ConApiAsync(proveedor);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var s = await SembrarAsync(api, EstadoRecibo.Guardado, null, ct);

        var r = await api.Cliente(api.Token(empresa: s.Empresa, permisos: Descargar)).GetAsync(Ruta(s), ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        r.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await r.Content.ReadAsByteArrayAsync(ct)).Should().Equal(Pdf);
        r.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        r.Content.Headers.ContentDisposition.FileNameStar.Should().Be("recibo-LHQ479-2024-2025.pdf");
        r.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        r.Headers.GetValues("Content-Security-Policy").Should().Equal("sandbox; default-src 'none'");
        r.Headers.CacheControl!.NoStore.Should().BeTrue();
        r.Headers.GetValues("Pragma").Should().Equal("no-cache");
        proveedor.Pedidos.Should().BeEmpty("se sirve desde el almacén de la suite, nunca por proxy al proveedor");
    }

    [Theory]
    [InlineData(EstadoRecibo.SinArchivo, "FILE_MISSING")]
    [InlineData(EstadoRecibo.SinArchivo, "NOT_FOUND")]
    [InlineData(EstadoRecibo.PendienteReintentoManual, "STORAGE_UNAVAILABLE")]
    [InlineData(EstadoRecibo.Pendiente, null)]
    public async Task Un_recibo_que_no_esta_GUARDADO_responde_409_RECIBO_NO_DISPONIBLE_con_su_motivo(EstadoRecibo estado, string? motivo)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var s = await SembrarAsync(api, estado, motivo, ct);

        var r = await api.Cliente(api.Token(empresa: s.Empresa, permisos: Descargar)).GetAsync(Ruta(s), ct);

        var cuerpo = await LiquidacionIndividualEndpointsTests.EsperarError(r, HttpStatusCode.Conflict, "RECIBO_NO_DISPONIBLE", "El recibo no está disponible.");
        if (motivo is null)
            cuerpo.GetProperty("motivo").ValueKind.Should().Be(JsonValueKind.Null);
        else
            cuerpo.GetProperty("motivo").GetString().Should().Be(motivo);
    }

    [Fact]
    public async Task Recibo_inexistente_de_otro_item_o_item_de_otro_lote_responden_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var s = await SembrarAsync(api, EstadoRecibo.Guardado, null, ct);
        var otro = await SembrarAsync(api, EstadoRecibo.Guardado, null, ct, s.Empresa);
        var cliente = api.Cliente(api.Token(empresa: s.Empresa, permisos: Descargar));

        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync(Ruta(s with { Recibo = Guid.NewGuid() }), ct),
            HttpStatusCode.NotFound, "RECIBO_NO_ENCONTRADO", "El recibo no existe.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync(Ruta(s with { Recibo = otro.Recibo }), ct),
            HttpStatusCode.NotFound, "RECIBO_NO_ENCONTRADO", "El recibo no existe.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync(Ruta(s with { Lote = otro.Lote }), ct),
            HttpStatusCode.NotFound, "ITEM_NO_ENCONTRADO", "La placa no existe en esta liquidación.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync(Ruta(s with { Lote = Guid.NewGuid() }), ct),
            HttpStatusCode.NotFound, "LIQUIDACION_NO_ENCONTRADA", "La liquidación no existe.");
    }

    [Fact]
    public async Task El_recibo_de_otra_compania_responde_404_igual_que_uno_inexistente()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var s = await SembrarAsync(api, EstadoRecibo.Guardado, null, ct);
        var otra = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, otra, ct);

        var r = await api.Cliente(api.Token(empresa: otra, permisos: Descargar)).GetAsync(Ruta(s), ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(r, HttpStatusCode.NotFound, "LIQUIDACION_NO_ENCONTRADA", "La liquidación no existe.");
    }

    [Fact]
    public async Task Sin_el_permiso_impuestos_recibos_descargar_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var s = await SembrarAsync(api, EstadoRecibo.Guardado, null, ct);

        var r = await api.Cliente(api.Token(empresa: s.Empresa, permisos: [Permisos.LiquidacionesVer])).GetAsync(Ruta(s), ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task El_Super_Admin_descarga_con_empresa_explicita_auditado_y_sin_ella_recibe_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var s = await SembrarAsync(api, EstadoRecibo.Guardado, null, ct);
        var sa = api.Cliente(api.Token(superAdmin: true, tipoEmpresa: "FLIT"));

        await LiquidacionIndividualEndpointsTests.EsperarError(await sa.GetAsync(Ruta(s), ct),
            HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.");
        var r = await sa.GetAsync($"{Ruta(s)}?empresa={s.Empresa}", ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        (await r.Content.ReadAsByteArrayAsync(ct)).Should().Equal(Pdf);
        await using var db = api.Contexto(s.Empresa);
        (await db.Set<LecturaBackoffice>().CountAsync(ct)).Should().BeGreaterThan(0, "la lectura del Super Admin queda auditada");
    }

    [Fact]
    public async Task Quien_no_es_Super_Admin_y_envia_empresa_recibe_403_EMPRESA_SOLO_SUPER_ADMIN()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var s = await SembrarAsync(api, EstadoRecibo.Guardado, null, ct);

        var r = await api.Cliente(api.Token(empresa: s.Empresa, permisos: Descargar)).GetAsync($"{Ruta(s)}?empresa={s.Empresa}", ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(r, HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.");
    }

    [Fact]
    public async Task Con_la_compania_suspendida_el_Super_Admin_sigue_descargando()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var s = await SembrarAsync(api, EstadoRecibo.Guardado, null, ct);
        api.Productos.Apagar(s.Empresa);

        var r = await api.Cliente(api.Token(superAdmin: true, tipoEmpresa: "FLIT")).GetAsync($"{Ruta(s)}?empresa={s.Empresa}", ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Nunca_devuelve_la_api_key_ni_en_el_cuerpo_ni_en_las_cabeceras()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ok = await SembrarAsync(api, EstadoRecibo.Guardado, null, ct);
        var no = await SembrarAsync(api, EstadoRecibo.SinArchivo, "FILE_MISSING", ct, ok.Empresa);
        var cliente = api.Cliente(api.Token(empresa: ok.Empresa, permisos: Descargar));

        foreach (var ruta in new[] { Ruta(ok), Ruta(no) })
        {
            var r = await cliente.GetAsync(ruta, ct);
            var cabeceras = string.Join("\n", r.Headers.Concat(r.Content.Headers).Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
            cabeceras.Should().NotContainEquivalentOf("X-API-Key").And.NotContain(ClienteApiLiquidacionTests.ApiKey);
            (await r.Content.ReadAsStringAsync(ct)).Should().NotContain(ClienteApiLiquidacionTests.ApiKey);
        }
    }

    // ── apoyo ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    internal sealed record Sembrado(Guid Empresa, Guid Lote, Guid Item, Guid Recibo);

    private static string Ruta(Sembrado s) => $"{Program.Prefijo}/liquidaciones/{s.Lote}/items/{s.Item}/recibos/{s.Recibo}";

    /// <summary>
    /// Compañía lista (o la dada), una individual con su ítem y un recibo VIGENCIA 2024–2025 en el estado pedido; si está GUARDADO, su
    /// PDF queda en el almacén de la suite como lo deja la descarga.
    /// </summary>
    private static async Task<Sembrado> SembrarAsync(ApiDePrueba api, EstadoRecibo estado, string? motivo, CancellationToken ct, Guid? empresaDada = null)
    {
        var empresa = empresaDada ?? Guid.NewGuid();
        if (empresaDada is null)
            await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["LHQ479"], ct, EstadoItem.Exitoso);
        ArchivoGuardado? guardado = null;
        if (estado == EstadoRecibo.Guardado)
        {
            await using var scope = api.App.Services.CreateAsyncScope();
            guardado = await scope.ServiceProvider.GetRequiredService<IAlmacenArchivos>().GuardarAsync(empresa, "recibo", "recibo.pdf", "application/pdf", Pdf, ct);
        }

        await using var db = api.Contexto(empresa);
        var recibo = new Recibo
        {
            ItemId = items[0],
            TenantId = empresa,
            FileIdApi = "f1",
            Tipo = TipoRecibo.Vigencia,
            Periodos = ["2024", "2025"],
            Estado = estado,
            Motivo = motivo,
            ReferenciaAlmacen = guardado?.Referencia,
            Sha256 = guardado?.Sha256,
            Tamano = guardado?.Tamano,
            Intentos = 1,
        };
        db.Add(recibo);
        await db.SaveChangesAsync(ct);
        return new Sembrado(empresa, lote, items[0], recibo.Id);
    }
}

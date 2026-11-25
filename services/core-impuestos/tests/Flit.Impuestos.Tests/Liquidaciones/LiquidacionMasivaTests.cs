using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Ejecucion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;

namespace Flit.Impuestos.Tests.Liquidaciones;

/// <summary>
/// HU #14694 (M12; F3-CF3, F3-CF9, delta 2 de impuestos.v1.yaml 1.1.0) — <c>POST /liquidaciones/masiva</c> revalida el archivo y crea
/// un lote <c>MASIVA</c> con un ítem <c>PENDIENTE</c> por placa única válida (con su fila), el nombre del archivo y la copia de la
/// configuración vigente al crear (F3-CF3), y libera el crédito (2 por compañía). Sin aprovisionamiento listo o con el producto
/// suspendido no crea nada (F3-CF9). <c>?empresa</c> sigue la regla única en masiva, analizar y plantilla. La plantilla es un .xlsx
/// con la hoja «Placas» y la columna «placa».
/// </summary>
/// <example>
/// <code>
/// var r = await cliente.PostAsync(EntornoMasiva.Masiva, EntornoMasiva.Archivo(EntornoMasiva.Csv("placa", "ABC123", "DEF456"), "placas.csv"), ct);
/// </code>
/// </example>
public sealed class LiquidacionMasivaTests
{
    private static readonly string[] Gestor = [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear];

    [Fact]
    public async Task Crea_el_lote_masiva_con_un_item_por_placa_unica_y_la_copia_de_la_configuracion_vigente()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        await M.SembrarConfiguracionAsync(api, empresa, ct, contractual: 400, operativo: 300, departamentos: ["05"]);
        var archivo = M.Csv("placa", "AAA111", "bbb-222", "aaa111", "X", "CCC333");

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor, usuario: usuario)).PostAsync(M.Masiva, M.Archivo(archivo, "flota marzo.csv"), ct);

        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.Created, texto);
        using var cuerpo = JsonDocument.Parse(texto);
        var raiz = cuerpo.RootElement;
        var id = raiz.GetProperty("id").GetGuid();
        raiz.GetProperty("tipo").GetString().Should().Be("MASIVA");
        raiz.GetProperty("estado").GetString().Should().Be("EN_PROCESO", "dos ítems recibieron crédito al crear");
        raiz.GetProperty("totalItems").GetInt32().Should().Be(3);
        raiz.GetProperty("nombreArchivo").GetString().Should().Be("flota marzo.csv");
        raiz.GetProperty("creadaPor").GetProperty("id").GetGuid().Should().Be(usuario);
        raiz.GetProperty("itemId").ValueKind.Should().Be(JsonValueKind.Null);
        raiz.GetProperty("motivoCierre").ValueKind.Should().Be(JsonValueKind.Null);
        raiz.TryGetProperty("items", out _).Should().BeFalse("los ítems de la masiva se paginan aparte");
        var copia = raiz.GetProperty("configCopia");
        copia.GetProperty("maxRegistrosLote").GetInt32().Should().Be(300);
        copia.GetProperty("departamentosHabilitados").EnumerateArray().Select(d => d.GetString()).Should().Equal("05");
        copia.GetProperty("concurrencia").GetInt32().Should().Be(2);
        r.Headers.Location!.ToString().Should().EndWith("/liquidaciones/" + id);

        var items = await M.ItemsAsync(api, empresa, id, ct);
        items.Select(i => (i.Fila, i.Placa)).Should().Equal((2, "AAA111"), (3, "BBB222"), (6, "CCC333"));
        items.Count(i => i.Estado == EstadoItem.ConsultandoRunt).Should().Be(2);
        items.Count(i => i.Estado == EstadoItem.Pendiente).Should().Be(1);
        (await EntornoEjecucion.SolicitudesAsync(api, ct)).Should().HaveCount(2);

        // F3-CF3: la copia es la del momento de crear; un cambio posterior de la configuración no la toca.
        await using (var db = api.Contexto(empresa))
        {
            await db.Set<ConfiguracionEmpresa>().ExecuteUpdateAsync(s => s.SetProperty(c => c.MaxRegistrosLoteOperativo, 50), ct);
        }

        using var detalle = JsonDocument.Parse(await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).GetStringAsync($"{M.Liquidaciones}/{id}", ct));
        detalle.RootElement.GetProperty("configCopia").GetProperty("maxRegistrosLote").GetInt32().Should().Be(300);
    }

    [Theory]
    [InlineData(null, "APROVISIONAMIENTO_NO_LISTO", "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.")]
    [InlineData(EstadoAprovisionamiento.Pendiente, "APROVISIONAMIENTO_NO_LISTO", "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.")]
    [InlineData(EstadoAprovisionamiento.RequiereCredenciales, "APROVISIONAMIENTO_NO_LISTO", "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.")]
    [InlineData(EstadoAprovisionamiento.Suspendido, "PRODUCTO_SUSPENDIDO", "Impuestos está suspendido para la compañía: solo lectura.")]
    public async Task Sin_aprovisionamiento_listo_o_suspendida_responde_409_y_no_crea_el_lote(EstadoAprovisionamiento? estado, string codigo, string literal)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        if (estado is { } e)
            await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct, e);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsync(M.Masiva, M.Archivo(M.Csv("placa", "ABC123"), "placas.csv"), ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(r, HttpStatusCode.Conflict, codigo, literal);
        (await M.LotesAsync(api, empresa, ct)).Should().Be(0);
    }

    [Fact]
    public async Task Un_archivo_sin_placas_validas_responde_422_ARCHIVO_SIN_PLACAS()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsync(M.Masiva, M.Archivo(M.Csv("placa", "", "A.B"), "placas.csv"), ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(r, HttpStatusCode.UnprocessableEntity, "ARCHIVO_SIN_PLACAS", "El archivo no tiene placas válidas.");
        (await M.LotesAsync(api, empresa, ct)).Should().Be(0);
    }

    [Fact]
    public async Task Delta_2_el_Super_Admin_necesita_empresa_en_masiva_analizar_y_plantilla_y_opera_sobre_esa_compania()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var superAdmin = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        await M.SembrarConfiguracionAsync(api, empresa, ct, contractual: 7);
        var sa = api.Cliente(api.Token(superAdmin: true, tipoEmpresa: "FLIT", usuario: superAdmin));
        var csv = M.Csv("placa", "ABC123", "DEF456");

        await LiquidacionIndividualEndpointsTests.EsperarError(await sa.PostAsync(M.Masiva, M.Archivo(csv, "p.csv"), ct), HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await sa.PostAsync(M.Analizar, M.Archivo(csv, "p.csv"), ct), HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await sa.GetAsync(M.Plantilla, ct), HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await sa.PostAsync($"{M.Masiva}?empresa={Guid.NewGuid()}", M.Archivo(csv, "p.csv"), ct),
            HttpStatusCode.NotFound, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos.");

        using (var analisis = JsonDocument.Parse(await (await sa.PostAsync($"{M.Analizar}?empresa={empresa}", M.Archivo(csv, "p.csv"), ct)).Content.ReadAsStringAsync(ct)))
            analisis.RootElement.GetProperty("tope").GetInt32().Should().Be(7, "el tope es el de la compañía elegida");
        (await sa.GetAsync($"{M.Plantilla}?empresa={empresa}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var r = await sa.PostAsync($"{M.Masiva}?empresa={empresa}", M.Archivo(csv, "p.csv"), ct);

        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync(ct));
        await using var db = api.Contexto(empresa);
        var lote = await db.Set<Liquidacion>().AsNoTracking().SingleAsync(ct);
        lote.TenantId.Should().Be(empresa);
        lote.CreadaPor.Should().Be(superAdmin);
        lote.Tipo.Should().Be(TipoLiquidacion.Masiva);
    }

    [Fact]
    public async Task Delta_2_quien_no_es_Super_Admin_y_manda_empresa_recibe_403_en_masiva_analizar_y_plantilla()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: Gestor));
        var csv = M.Csv("placa", "ABC123");

        foreach (var r in new[]
                 {
                     await cliente.PostAsync($"{M.Masiva}?empresa={empresa}", M.Archivo(csv, "p.csv"), ct),
                     await cliente.PostAsync($"{M.Analizar}?empresa={empresa}", M.Archivo(csv, "p.csv"), ct),
                     await cliente.GetAsync($"{M.Plantilla}?empresa={empresa}", ct),
                 })
        {
            await LiquidacionIndividualEndpointsTests.EsperarError(r, HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.");
        }

        (await M.LotesAsync(api, empresa, ct)).Should().Be(0);
    }

    [Fact]
    public async Task La_plantilla_es_un_xlsx_con_la_hoja_Placas_y_la_columna_placa_que_el_analizador_acepta()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: Gestor));

        var r = await cliente.GetAsync(M.Plantilla, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be(M.XlsxContentType);
        var bytes = await r.Content.ReadAsByteArrayAsync(ct);
        var (hoja, filas) = M.LeerXlsx(bytes);
        hoja.Should().Be("Placas");
        filas.Should().ContainSingle().Which.Should().Equal("placa");
        var analizada = await cliente.PostAsync(M.Analizar, M.Archivo(bytes, "plantilla.xlsx"), ct);
        analizada.StatusCode.Should().Be(HttpStatusCode.OK, "la plantilla vacía tiene la columna");
        (await api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.LiquidacionesVer])).GetAsync(M.Plantilla, ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }
}

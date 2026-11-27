using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Ejecucion;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;

namespace Flit.Impuestos.Tests.Api;

/// <summary>
/// HU #14694 (R3, decisión del usuario 2026-11-26; impuestos.v1.yaml 1.3.0) — <c>POST /liquidaciones/{id}/items/{itemId}/relanzar</c>
/// abre un nuevo intento del MISMO ítem: no crea otra liquidación. Solo desde <c>INCIERTO_REQUIERE_DECISION</c>,
/// <c>DATOS_INCOMPLETOS</c>, <c>ERROR</c> con clase <c>REINTENTABLE</c> o <c>ERROR</c> <c>portal_rejected</c> de placa no inscrita (si
/// no, 409 <c>NO_RELANZABLE</c>). Guarda la foto del intento que cierra en <c>liquidacion_item_intentos</c>, sube
/// <c>numero_intento</c>, limpia error, resultado, total, warnings, token, reintentos y vigencias, devuelve el ítem a <c>PENDIENTE</c>
/// (y el despachador lo pasa a la cola con un evento nuevo), recalcula el lote como en curso y responde 200 con la misma liquidación.
/// <c>confirmarPosibleDuplicado</c> se acepta y se ignora. Exige el aprovisionamiento como la individual y sigue la regla de
/// <c>?empresa</c>.
/// </summary>
/// <example>
/// <code>
/// var r = await cliente.PostAsync($"/api/v1/impuestos/liquidaciones/{lote}/items/{item}/relanzar", null, ct); // 200
/// </code>
/// </example>
public sealed class RelanzarItemTests
{
    public const string LiteralNoRelanzable =
        "Solo se relanzan placas en «Incierto — requiere decisión», con datos incompletos, con error reintentable o con placa no inscrita, que no estén en curso y cuya liquidación no se haya cerrado a mano.";

    private static readonly string[] Gestor = [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear];
    private static readonly object Confirmado = new { confirmarPosibleDuplicado = true };
    private static readonly string[] WarningsPrevios = ["period_not_liquidable"];
    private const string PlacaNoInscrita = "EviewDetailVehicles: La placa ingresada no se encuentra inscrita en nuestra base de datos.";

    [Theory]
    [InlineData(EstadoItem.InciertoRequiereDecision, null)]
    [InlineData(EstadoItem.Error, ClaseError.Reintentable)]
    [InlineData(EstadoItem.DatosIncompletos, null)]
    [InlineData(EstadoItem.Error, ClaseError.Depende, "portal_rejected", PlacaNoInscrita)]
    public async Task Relanza_el_mismo_item_como_nuevo_intento_y_responde_200_con_la_misma_liquidacion(
        EstadoItem estado, ClaseError? clase, string? codigo = null, string? mensaje = null)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, estado, clase, ct, codigo, mensaje, conResultado: true);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync(Ruta(lote, item), Confirmado, ct);

        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        using (var cuerpo = JsonDocument.Parse(texto))
        {
            var raiz = cuerpo.RootElement;
            raiz.GetProperty("id").GetGuid().Should().Be(lote, "LiquidacionConItems de la MISMA liquidación");
            raiz.GetProperty("estado").GetString().Should().Be("EN_PROCESO", "el lote vuelve a estar en curso");
            raiz.GetProperty("contadores").GetProperty("pendientes").GetInt32().Should().Be(1);
            raiz.GetProperty("contadores").GetProperty("exitosas").GetInt32().Should().Be(1);
            raiz.GetProperty("items").GetArrayLength().Should().Be(2);
            var relanzado = raiz.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == item);
            relanzado.GetProperty("posibleDuplicado").GetBoolean().Should().BeFalse("ya no se marca posible duplicado");
            relanzado.GetProperty("errorCodigo").ValueKind.Should().Be(JsonValueKind.Null);
        }

        (await M.LotesAsync(api, empresa, ct)).Should().Be(1, "no se crea otra liquidación");
        var fila = await EntornoEjecucion.ItemAsync(api, empresa, item, ct);
        fila.NumeroIntento.Should().Be(2);
        fila.Estado.Should().Be(EstadoItem.ConsultandoRunt, "nace PENDIENTE como al crear y el despachador le dio crédito");
        fila.ErrorCodigo.Should().BeNull();
        fila.ErrorClase.Should().BeNull();
        fila.ErrorMensaje.Should().BeNull();
        fila.CampoFaltante.Should().BeNull();
        fila.TotalAPagar.Should().BeNull();
        fila.ResultadoStatus.Should().BeNull();
        fila.ResultadoCifrado.Should().BeNull();
        fila.Warnings.Should().BeEmpty();
        fila.TokenHash.Should().BeNull();
        fila.ConsultaApiId.Should().BeNull();
        fila.IntentosRunt.Should().Be(0);
        fila.IntentosReenvio.Should().Be(0);
        fila.PosibleDuplicado.Should().BeFalse();
        fila.RelanzadoDesde.Should().BeNull();

        await using (var db = api.Contexto(empresa))
        {
            (await db.Set<LiquidacionPeriodo>().CountAsync(p => p.ItemId == item, ct)).Should().Be(0, "las vigencias del intento anterior se borran");
            var intento = await db.Set<LiquidacionItemIntento>().AsNoTracking().SingleAsync(i => i.ItemId == item, ct);
            intento.Numero.Should().Be(1);
            intento.TenantId.Should().Be(empresa);
            intento.Estado.Should().Be(estado);
            intento.ErrorCodigo.Should().Be(codigo ?? "CODIGO_PREVIO");
            intento.ErrorClase.Should().Be(clase);
            intento.ErrorMensaje.Should().Be(mensaje ?? "Mensaje previo.");
            intento.CampoFaltante.Should().Be("OwnerEmail");
            intento.TotalAPagar.Should().Be(1234.5m);
            intento.RelanzadoPor.Should().Be("operador@empresa.test");
            intento.RegistradoEn.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        }

        var cabecera = await EntornoEjecucion.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.EnProceso);
        cabecera.TerminadaEn.Should().BeNull("volverá a terminar cuando cierre el nuevo intento");
        cabecera.TotalItems.Should().Be(2);
        var solicitud = (await EntornoEjecucion.SolicitudesAsync(api, ct)).Should().ContainSingle("el nuevo intento entra a la cola").Subject;
        EventEnvelope.FromJson(System.Text.Encoding.UTF8.GetBytes(solicitud.Payload)).DataAs<LiquidacionSolicitada>()
            .Should().Be(new LiquidacionSolicitada(lote, item));

        var d = await DetalleAsync(api, empresa, lote, item, ct);
        d.GetProperty("numeroIntento").GetInt32().Should().Be(2);
        var intentos = d.GetProperty("intentos").EnumerateArray().ToList();
        intentos.Should().ContainSingle();
        intentos[0].GetProperty("numero").GetInt32().Should().Be(1);
        intentos[0].GetProperty("estado").GetString().Should().Be(Codigo(estado));
        intentos[0].GetProperty("errorCodigo").GetString().Should().Be(codigo ?? "CODIGO_PREVIO");
        intentos[0].GetProperty("errorMensaje").GetString().Should().Be(mensaje ?? "Mensaje previo.");
        intentos[0].GetProperty("totalAPagar").GetDecimal().Should().Be(1234.5m);
        intentos[0].GetProperty("relanzadoPor").GetString().Should().Be("operador@empresa.test");
        intentos[0].TryGetProperty("registradoEn", out var registrado).Should().BeTrue();
        registrado.GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        d.GetProperty("periodos").GetArrayLength().Should().Be(0);
        d.GetProperty("resultado").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ConfirmarPosibleDuplicado_se_acepta_y_se_ignora(int variante)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, EstadoItem.InciertoRequiereDecision, null, ct);
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: Gestor));

        var r = variante switch
        {
            0 => await cliente.PostAsJsonAsync(Ruta(lote, item), new { confirmarPosibleDuplicado = false }, ct),
            1 => await cliente.PostAsJsonAsync(Ruta(lote, item), new { }, ct),
            _ => await cliente.PostAsync(Ruta(lote, item), null, ct),
        };

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        (await EntornoEjecucion.ItemAsync(api, empresa, item, ct)).NumeroIntento.Should().Be(2);
    }

    [Theory]
    [InlineData(EstadoItem.Error, ClaseError.CorregirDatos)]
    [InlineData(EstadoItem.Error, ClaseError.Depende)]
    [InlineData(EstadoItem.Exitoso, null)]
    [InlineData(EstadoItem.Enviado, null)]
    [InlineData(EstadoItem.Incierto, null)]
    [InlineData(EstadoItem.Pendiente, null)]
    [InlineData(EstadoItem.ConsultandoRunt, null)]
    [InlineData(EstadoItem.NoAplica, null)]
    [InlineData(EstadoItem.Error, ClaseError.Depende, "portal_rejected", "El portal rechazó la consulta por mantenimiento.")]
    public async Task Desde_otro_estado_responde_409_NO_RELANZABLE_y_no_cambia_nada(
        EstadoItem estado, ClaseError? clase, string? codigo = null, string? mensaje = null)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, estado, clase, ct, codigo, mensaje);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync(Ruta(lote, item), Confirmado, ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(r, HttpStatusCode.Conflict, "NO_RELANZABLE", LiteralNoRelanzable);
        await NadaCambioAsync(api, empresa, lote, item, estado, ct);
    }

    [Fact]
    public async Task Un_segundo_relanzamiento_seguido_encuentra_el_item_en_vuelo_y_responde_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, EstadoItem.InciertoRequiereDecision, null, ct);
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: Gestor));

        var primero = await cliente.PostAsJsonAsync(Ruta(lote, item), Confirmado, ct);
        var segundo = await cliente.PostAsJsonAsync(Ruta(lote, item), Confirmado, ct);

        primero.StatusCode.Should().Be(HttpStatusCode.OK, await primero.Content.ReadAsStringAsync(ct));
        await LiquidacionIndividualEndpointsTests.EsperarError(segundo, HttpStatusCode.Conflict, "NO_RELANZABLE", LiteralNoRelanzable);
        (await EntornoEjecucion.ItemAsync(api, empresa, item, ct)).NumeroIntento.Should().Be(2);
        await using var db = api.Contexto(empresa);
        (await db.Set<LiquidacionItemIntento>().CountAsync(i => i.ItemId == item, ct)).Should().Be(1);
        (await EntornoEjecucion.SolicitudesAsync(api, ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task En_una_liquidacion_cerrada_a_mano_responde_409_NO_RELANZABLE()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, EstadoItem.DatosIncompletos, null, ct);
        await using (var db = api.Contexto(empresa))
            await db.Set<Liquidacion>().Where(l => l.Id == lote).ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, EstadoLote.CerradoManualmente), ct);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync(Ruta(lote, item), Confirmado, ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(r, HttpStatusCode.Conflict, "NO_RELANZABLE", LiteralNoRelanzable);
        (await EntornoEjecucion.ItemAsync(api, empresa, item, ct)).NumeroIntento.Should().Be(1);
    }

    [Fact]
    public async Task El_nuevo_intento_pasa_por_la_cola_con_un_evento_nuevo_y_llega_al_proveedor()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoEjecucion.ProveedorQueAcepta();
        var runt = new RuntDePrueba { Responder = _ => new ConsultaRunt.Encontrado(RuntDePrueba.Cundinamarca(conCorreo: false)) };
        await using var api = await M.ConApiAsync(proveedor, runt);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["LHQ479"], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);
        await EntornoEjecucion.ProcesarColaAsync(api, ct);
        (await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct)).Estado.Should().Be(EstadoItem.DatosIncompletos);
        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).Estado.Should().Be(EstadoLote.CompletadoConNovedades);
        runt.Responder = _ => new ConsultaRunt.Encontrado(RuntDePrueba.Antioquia());

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsync(Ruta(lote, items[0]), null, ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        var solicitudes = await EntornoEjecucion.SolicitudesAsync(api, ct);
        solicitudes.Should().HaveCount(2);
        solicitudes.Select(m => EventEnvelope.FromJson(System.Text.Encoding.UTF8.GetBytes(m.Payload)).EventId).Distinct().Should().HaveCount(2, "eventId nuevo: la bandeja no lo descarta");
        EntornoEjecucion.Envios(proveedor).Should().Be(1);
        var fila = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        fila.Estado.Should().Be(EstadoItem.Enviado);
        fila.NumeroIntento.Should().Be(2);
        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).Estado.Should().Be(EstadoLote.EnProceso);
        var d = await DetalleAsync(api, empresa, lote, items[0], ct);
        var intento = d.GetProperty("intentos").EnumerateArray().Should().ContainSingle().Subject;
        intento.GetProperty("estado").GetString().Should().Be("DATOS_INCOMPLETOS");
        intento.GetProperty("totalAPagar").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Los_intentos_van_del_mas_reciente_al_mas_antiguo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, EstadoItem.InciertoRequiereDecision, null, ct);
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: Gestor));
        (await cliente.PostAsync(Ruta(lote, item), null, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var db = api.Contexto(empresa))
            await db.Set<LiquidacionItem>().Where(i => i.Id == item).ExecuteUpdateAsync(s => s.SetProperty(i => i.Estado, EstadoItem.Error)
                .SetProperty(i => i.ErrorClase, ClaseError.Reintentable).SetProperty(i => i.ErrorCodigo, "RUNT_NO_DISPONIBLE"), ct);

        (await cliente.PostAsync(Ruta(lote, item), null, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var d = await DetalleAsync(api, empresa, lote, item, ct);
        d.GetProperty("numeroIntento").GetInt32().Should().Be(3);
        var intentos = d.GetProperty("intentos").EnumerateArray().ToList();
        intentos.Select(i => i.GetProperty("numero").GetInt32()).Should().Equal(2, 1);
        intentos[0].GetProperty("estado").GetString().Should().Be("ERROR");
        intentos[0].GetProperty("errorCodigo").GetString().Should().Be("RUNT_NO_DISPONIBLE");
        intentos[1].GetProperty("estado").GetString().Should().Be("INCIERTO_REQUIERE_DECISION");
    }

    [Fact]
    public async Task Sin_aprovisionamiento_listo_responde_409_como_la_individual()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, EstadoItem.InciertoRequiereDecision, null, ct);
        await using (var db = api.Contexto(empresa))
            await db.Set<AprovisionamientoEmpresa>().ExecuteUpdateAsync(s => s.SetProperty(a => a.Estado, EstadoAprovisionamiento.RequiereCredenciales), ct);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync(Ruta(lote, item), Confirmado, ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(r, HttpStatusCode.Conflict, "APROVISIONAMIENTO_NO_LISTO",
            "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.");
        await NadaCambioAsync(api, empresa, lote, item, EstadoItem.InciertoRequiereDecision, ct);
    }

    [Fact]
    public async Task Item_inexistente_o_de_otra_compania_responde_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, EstadoItem.InciertoRequiereDecision, null, ct);
        var otra = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, otra, ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(
            await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync(Ruta(lote, Guid.NewGuid()), Confirmado, ct),
            HttpStatusCode.NotFound, "ITEM_NO_ENCONTRADO", "La placa no existe en esta liquidación.");
        await LiquidacionIndividualEndpointsTests.EsperarError(
            await api.Cliente(api.Token(empresa: otra, permisos: Gestor)).PostAsJsonAsync(Ruta(lote, item), Confirmado, ct),
            HttpStatusCode.NotFound, "LIQUIDACION_NO_ENCONTRADA", "La liquidación no existe.");
        await NadaCambioAsync(api, empresa, lote, item, EstadoItem.InciertoRequiereDecision, ct);
    }

    [Fact]
    public async Task Delta_2_el_Super_Admin_relanza_con_empresa_y_quien_no_lo_es_recibe_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, EstadoItem.InciertoRequiereDecision, null, ct);
        var sa = api.Cliente(api.Token(superAdmin: true, tipoEmpresa: "FLIT"));

        await LiquidacionIndividualEndpointsTests.EsperarError(
            await sa.PostAsJsonAsync(Ruta(lote, item), Confirmado, ct), HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.");
        await LiquidacionIndividualEndpointsTests.EsperarError(
            await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync($"{Ruta(lote, item)}?empresa={empresa}", Confirmado, ct),
            HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.");
        var r = await sa.PostAsJsonAsync($"{Ruta(lote, item)}?empresa={empresa}", Confirmado, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        (await M.LotesAsync(api, empresa, ct)).Should().Be(1);
        (await EntornoEjecucion.ItemAsync(api, empresa, item, ct)).NumeroIntento.Should().Be(2);
    }

    private static string Ruta(Guid lote, Guid item) => $"{Program.Prefijo}/liquidaciones/{lote}/items/{item}/relanzar";

    private static string Codigo(EstadoItem estado) => JsonNamingPolicy.SnakeCaseUpper.ConvertName(estado.ToString());

    private static async Task<JsonElement> DetalleAsync(ApiDePrueba api, Guid empresa, Guid lote, Guid item, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await api.Cliente(api.Token(empresa: empresa, permisos: Gestor))
            .GetStringAsync($"{M.Liquidaciones}/{lote}/items/{item}", ct));
        return doc.RootElement.Clone();
    }

    /// <summary>Un rechazo no deja intento, no sube el número, no crea liquidación ni encola nada.</summary>
    private static async Task NadaCambioAsync(ApiDePrueba api, Guid empresa, Guid lote, Guid item, EstadoItem estado, CancellationToken ct)
    {
        var fila = await EntornoEjecucion.ItemAsync(api, empresa, item, ct);
        fila.Estado.Should().Be(estado);
        fila.NumeroIntento.Should().Be(1);
        await using var db = api.Contexto(empresa);
        (await db.Set<LiquidacionItemIntento>().CountAsync(i => i.ItemId == item, ct)).Should().Be(0);
        (await M.LotesAsync(api, empresa, ct)).Should().Be(1);
        (await EntornoEjecucion.SolicitudesAsync(api, ct)).Should().BeEmpty();
        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).Estado.Should().Be(EstadoLote.CompletadoConNovedades);
    }

    /// <summary>
    /// Compañía lista y un lote MASIVA terminado de dos ítems; el segundo (AAA001) queda en <paramref name="estado"/>. Con
    /// <paramref name="conResultado"/> el ítem trae además lo que un intento deja: error, campo faltante, total, resultado cifrado,
    /// warnings, token, consulta, reintentos y una vigencia.
    /// </summary>
    private static async Task<(Guid Empresa, Guid Lote, Guid Item)> SembrarAsync(
        ApiDePrueba api, EstadoItem estado, ClaseError? clase, CancellationToken ct, string? codigo = null, string? mensaje = null, bool conResultado = false)
    {
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["AAA000", "AAA001"], ct, EstadoItem.Exitoso);
        await using var db = api.Contexto(empresa);
        if (conResultado)
        {
            await db.Set<LiquidacionItem>().Where(i => i.Id == items[1])
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Estado, estado).SetProperty(i => i.ErrorClase, clase)
                    .SetProperty(i => i.ErrorCodigo, codigo ?? "CODIGO_PREVIO").SetProperty(i => i.ErrorMensaje, mensaje ?? "Mensaje previo.")
                    .SetProperty(i => i.CampoFaltante, "OwnerEmail").SetProperty(i => i.TotalAPagar, 1234.5m)
                    .SetProperty(i => i.ResultadoStatus, "con_deuda").SetProperty(i => i.ResultadoCifrado, new byte[] { 1, 2, 3 })
                    .SetProperty(i => i.Warnings, WarningsPrevios).SetProperty(i => i.TokenHash, new byte[] { 9, 9, 9 })
                    .SetProperty(i => i.ConsultaApiId, "consulta-previa").SetProperty(i => i.IntentosRunt, 2).SetProperty(i => i.IntentosReenvio, 1), ct);
            db.Add(new LiquidacionPeriodo { ItemId = items[1], TenantId = empresa, Periodo = "2025", TotalAPagar = 1234.5m });
            await db.SaveChangesAsync(ct);
        }
        else
        {
            await db.Set<LiquidacionItem>().Where(i => i.Id == items[1])
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Estado, estado).SetProperty(i => i.ErrorClase, clase)
                    .SetProperty(i => i.ErrorCodigo, codigo).SetProperty(i => i.ErrorMensaje, mensaje), ct);
        }

        await db.Set<Liquidacion>().Where(l => l.Id == lote).ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, EstadoLote.CompletadoConNovedades)
            .SetProperty(l => l.TerminadaEn, DateTimeOffset.UtcNow).SetProperty(l => l.CExitosas, 1), ct);
        return (empresa, lote, items[1]);
    }
}

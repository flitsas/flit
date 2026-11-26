using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Ejecucion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;

namespace Flit.Impuestos.Tests.Api;

/// <summary>
/// HU #14694 (M10 diferido a M12; plan §3.2, deltas 2 y 5 de impuestos.v1.yaml 1.1.0) —
/// <c>POST /liquidaciones/{id}/items/{itemId}/relanzar</c> con <c>{confirmarPosibleDuplicado: true}</c>: solo desde
/// <c>INCIERTO_REQUIERE_DECISION</c>, <c>DATOS_INCOMPLETOS</c>, <c>ERROR</c> con clase <c>REINTENTABLE</c> o <c>ERROR</c>
/// <c>portal_rejected</c> de placa no inscrita (2026-11-26; si no, 409 <c>NO_RELANZABLE</c>); sin la
/// confirmación, 422 <c>CONFIRMACION_REQUERIDA</c>. Crea una liquidación INDIVIDUAL nueva (201) con la misma placa,
/// <c>posibleDuplicado=true</c> y <c>relanzadoDesdeItemId</c> = <c>relanzadoDesde</c> = el ítem original, que no cambia. Exige el
/// aprovisionamiento como la individual y sigue la regla de <c>?empresa</c>.
/// </summary>
/// <example>
/// <code>
/// var r = await cliente.PostAsJsonAsync($"/api/v1/impuestos/liquidaciones/{lote}/items/{item}/relanzar", new { confirmarPosibleDuplicado = true }, ct);
/// </code>
/// </example>
public sealed class RelanzarItemTests
{
    private static readonly string[] Gestor = [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear];
    private static readonly object Confirmado = new { confirmarPosibleDuplicado = true };
    private const string PlacaNoInscrita = "EviewDetailVehicles: La placa ingresada no se encuentra inscrita en nuestra base de datos.";

    [Theory]
    [InlineData(EstadoItem.InciertoRequiereDecision, null)]
    [InlineData(EstadoItem.Error, ClaseError.Reintentable)]
    [InlineData(EstadoItem.DatosIncompletos, null)]
    [InlineData(EstadoItem.Error, ClaseError.Depende, "portal_rejected", PlacaNoInscrita)]
    public async Task Relanza_como_individual_nueva_con_posible_duplicado_y_el_original_no_cambia(
        EstadoItem estado, ClaseError? clase, string? codigo = null, string? mensaje = null)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, estado, clase, ct, codigo, mensaje);
        var usuario = Guid.NewGuid();

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor, usuario: usuario)).PostAsJsonAsync(Ruta(lote, item), Confirmado, ct);

        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.Created, texto);
        using var cuerpo = JsonDocument.Parse(texto);
        var raiz = cuerpo.RootElement;
        var nuevo = raiz.GetProperty("id").GetGuid();
        nuevo.Should().NotBe(lote);
        raiz.GetProperty("tipo").GetString().Should().Be("INDIVIDUAL");
        raiz.GetProperty("creadaPor").GetProperty("id").GetGuid().Should().Be(usuario);
        raiz.GetProperty("creadaPor").GetProperty("nombre").GetString().Should().Be("operador@empresa.test", "creada_por_nombre (1.2.0)");
        var nuevoItem = raiz.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        nuevoItem.GetProperty("placa").GetString().Should().Be("AAA001");
        nuevoItem.GetProperty("posibleDuplicado").GetBoolean().Should().BeTrue();
        nuevoItem.GetProperty("relanzadoDesdeItemId").GetGuid().Should().Be(item);
        nuevoItem.GetProperty("relanzadoDesde").GetGuid().Should().Be(item, "relanzadoDesde se conserva con el mismo valor (delta 5)");
        r.Headers.Location!.ToString().Should().EndWith("/liquidaciones/" + nuevo);

        var original = await EntornoEjecucion.ItemAsync(api, empresa, item, ct);
        original.Estado.Should().Be(estado);
        original.PosibleDuplicado.Should().BeFalse();
        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).TotalItems.Should().Be(2, "el lote original no cambia");
        (await EntornoEjecucion.SolicitudesAsync(api, ct)).Should().ContainSingle("el ítem nuevo recibió crédito");

        using var items = JsonDocument.Parse(await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).GetStringAsync($"{M.Liquidaciones}/{nuevo}/items", ct));
        items.RootElement.GetProperty("items")[0].GetProperty("relanzadoDesdeItemId").GetGuid().Should().Be(item);
    }

    [Theory]
    [InlineData(EstadoItem.Error, ClaseError.CorregirDatos)]
    [InlineData(EstadoItem.Error, ClaseError.Depende)]
    [InlineData(EstadoItem.Exitoso, null)]
    [InlineData(EstadoItem.Enviado, null)]
    [InlineData(EstadoItem.Incierto, null)]
    [InlineData(EstadoItem.Error, ClaseError.Depende, "portal_rejected", "El portal rechazó la consulta por mantenimiento.")]
    public async Task Desde_otro_estado_responde_409_NO_RELANZABLE_y_no_crea_nada(
        EstadoItem estado, ClaseError? clase, string? codigo = null, string? mensaje = null)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, estado, clase, ct, codigo, mensaje);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync(Ruta(lote, item), Confirmado, ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(r, HttpStatusCode.Conflict, "NO_RELANZABLE",
            "Solo se relanzan placas en «Incierto — requiere decisión» o con error reintentable.");
        (await M.LotesAsync(api, empresa, ct)).Should().Be(1);
    }

    [Fact]
    public async Task Sin_confirmar_el_posible_duplicado_responde_422_CONFIRMACION_REQUERIDA()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, EstadoItem.InciertoRequiereDecision, null, ct);
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: Gestor));
        const string Literal = "Confirma que aceptas un posible duplicado en el proveedor.";

        await LiquidacionIndividualEndpointsTests.EsperarError(
            await cliente.PostAsJsonAsync(Ruta(lote, item), new { confirmarPosibleDuplicado = false }, ct), HttpStatusCode.UnprocessableEntity, "CONFIRMACION_REQUERIDA", Literal);
        await LiquidacionIndividualEndpointsTests.EsperarError(
            await cliente.PostAsJsonAsync(Ruta(lote, item), new { }, ct), HttpStatusCode.UnprocessableEntity, "CONFIRMACION_REQUERIDA", Literal);
        await LiquidacionIndividualEndpointsTests.EsperarError(
            await cliente.PostAsync(Ruta(lote, item), null, ct), HttpStatusCode.UnprocessableEntity, "CONFIRMACION_REQUERIDA", Literal);
        (await M.LotesAsync(api, empresa, ct)).Should().Be(1);
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

        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync(ct));
        (await M.LotesAsync(api, empresa, ct)).Should().Be(2);
    }

    private static string Ruta(Guid lote, Guid item) => $"{Program.Prefijo}/liquidaciones/{lote}/items/{item}/relanzar";

    /// <summary>Compañía lista y un lote MASIVA terminado de dos ítems; el segundo (AAA001) queda en <paramref name="estado"/>.</summary>
    private static async Task<(Guid Empresa, Guid Lote, Guid Item)> SembrarAsync(ApiDePrueba api, EstadoItem estado, ClaseError? clase, CancellationToken ct, string? codigo = null, string? mensaje = null)
    {
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["AAA000", "AAA001"], ct, EstadoItem.Exitoso);
        await using var db = api.Contexto(empresa);
        await db.Set<LiquidacionItem>().Where(i => i.Id == items[1])
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Estado, estado).SetProperty(i => i.ErrorClase, clase)
                .SetProperty(i => i.ErrorCodigo, codigo).SetProperty(i => i.ErrorMensaje, mensaje), ct);
        await db.Set<Liquidacion>().Where(l => l.Id == lote).ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, EstadoLote.CompletadoConNovedades), ct);
        return (empresa, lote, items[1]);
    }
}

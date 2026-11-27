using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Liquidaciones;

/// <summary>Filtros del grid (<c>GET /liquidaciones</c>) ya validados.</summary>
internal sealed record FiltrosLiquidaciones(TipoLiquidacion? Tipo, EstadoLote? Estado, RangoDias Rango, string? Placa, int Page, int PageSize);

/// <summary>
/// Lecturas de las liquidaciones de UNA compañía (plan §5.1; F2a-CF9/CF10): siempre con el contexto de esa compañía
/// (<see cref="AlmacenCredenciales.ContextoDe"/>), así que el filtro de empresa vale igual para el Super Admin que elige otra con
/// <c>?empresa</c> (sin <c>IgnoreQueryFilters</c>). Una liquidación de otra compañía no existe: 404 <c>LIQUIDACION_NO_ENCONTRADA</c>;
/// un ítem que no es del lote, 404 <c>ITEM_NO_ENCONTRADO</c>. La PII del propietario se descifra para mostrarla completa (DA-7=b).
/// <c>creadaPor.nombre</c> y <c>cerradaPor.nombre</c> salen de <c>creada_por_nombre</c> y <c>cerrada_por_nombre</c> (1.2.0; vacíos en
/// filas anteriores) y <c>motivoCierre</c> de <c>motivo_cierre</c> (<see cref="MotivoCierreDe"/>, el único punto que la lee).
/// <c>relanzadoDesdeLiquidacionId</c> sale del join con el ítem original.
/// </summary>
/// <example>
/// <code>
/// var pagina = await consulta.ListarAsync(empresa, filtros, new ReferenciaDto(empresa, "Flota Andina SAS"), ct);
/// var detalle = await consulta.ItemDetalleAsync(empresa, lote, item, empresaRef, ct);
/// </code>
/// </example>
public sealed class ConsultaLiquidaciones(AlmacenCredenciales almacen, IProteccionImpuestos proteccion)
{
    /// <summary>Campos de <c>validations[]</c> con datos de contacto del propietario: nunca salen en una respuesta.</summary>
    public static IReadOnlySet<string> CamposDeContacto { get; } = new HashSet<string>(StringComparer.Ordinal) { "OwnerEmail", "OwnerPhone", "OwnerAddress" };

    internal async Task<PaginaDto<ResumenLiquidacionDto>> ListarAsync(Guid empresa, FiltrosLiquidaciones f, ReferenciaDto empresaRef, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(f);
        await using var db = almacen.ContextoDe(empresa);
        var q = db.Set<Liquidacion>().AsNoTracking().Where(l => l.CreadaEn >= f.Rango.Inicio && l.CreadaEn < f.Rango.Fin);
        if (f.Tipo is { } tipo)
            q = q.Where(l => l.Tipo == tipo);
        if (f.Estado is { } estado)
            q = q.Where(l => l.Estado == estado);
        if (!string.IsNullOrEmpty(f.Placa))
        {
            var placa = f.Placa;
            q = q.Where(l => db.Set<LiquidacionItem>().Any(i => i.LiquidacionId == l.Id && i.Placa.Contains(placa)));
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var filas = await q.OrderByDescending(l => l.CreadaEn).ThenByDescending(l => l.Id)
            .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync(ct).ConfigureAwait(false);
        var individuales = await ItemsIndividualesAsync(db, filas, ct).ConfigureAwait(false);
        return new PaginaDto<ResumenLiquidacionDto>([.. filas.Select(l => Resumen(l, empresaRef, individuales.GetValueOrDefault(l.Id)))], total, f.Page, f.PageSize);
    }

    public async Task<LiquidacionDto> LiquidacionAsync(Guid empresa, Guid id, ReferenciaDto empresaRef, CancellationToken ct)
    {
        await using var db = almacen.ContextoDe(empresa);
        var lote = await db.Set<Liquidacion>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false)
            ?? throw ErroresLiquidaciones.NoEncontrada();
        var individuales = await ItemsIndividualesAsync(db, [lote], ct).ConfigureAwait(false);
        return Detalle(lote, empresaRef, individuales.GetValueOrDefault(lote.Id));
    }

    /// <summary>La liquidación con sus ítems (respuesta 201 de la individual y del relanzamiento).</summary>
    public async Task<LiquidacionConItemsDto> ConItemsAsync(Guid empresa, Guid id, ReferenciaDto empresaRef, CancellationToken ct)
    {
        var d = await LiquidacionAsync(empresa, id, empresaRef, ct).ConfigureAwait(false);
        var items = await ItemsAsync(empresa, id, null, null, 1, 200, ct).ConfigureAwait(false);
        return new LiquidacionConItemsDto(d.Id, d.Tipo, d.Estado, d.TotalItems, d.Contadores, d.CreadaEn, d.CreadaPor, d.Empresa, d.NombreArchivo, d.Placa,
            d.ItemId, d.EstadoItem, d.PlacaNoInscrita, d.TerminadaEn, d.CerradaPor, d.CerradaEn, d.MotivoCierre, d.ConfigCopia, items.Items);
    }

    /// <summary>Placas del lote por fila del archivo (o por placa en la individual); 404 si el lote no es de la compañía.</summary>
    public async Task<PaginaDto<ItemDto>> ItemsAsync(Guid empresa, Guid id, EstadoItem? estado, string? placa, int page, int pageSize, CancellationToken ct)
    {
        await using var db = almacen.ContextoDe(empresa);
        if (!await db.Set<Liquidacion>().AnyAsync(l => l.Id == id, ct).ConfigureAwait(false))
            throw ErroresLiquidaciones.NoEncontrada();
        var q = db.Set<LiquidacionItem>().AsNoTracking().Where(i => i.LiquidacionId == id);
        if (estado is { } e)
            q = q.Where(i => i.Estado == e);
        if (!string.IsNullOrEmpty(placa))
            q = q.Where(i => i.Placa.Contains(placa));
        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var filas = await q.OrderBy(i => i.Fila).ThenBy(i => i.Placa).ThenBy(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        var origenes = await LotesDeOrigenAsync(db, filas, ct).ConfigureAwait(false);
        return new PaginaDto<ItemDto>([.. filas.Select(i => Item(i, origenes))], total, page, pageSize);
    }

    /// <summary>
    /// Detalle de una placa (esquema <c>ItemDetalle</c>): el ítem, el <c>result</c> del proveedor tal cual (con <c>file_errors</c> de
    /// la consulta y sin los campos de contacto de <c>validations</c>), las vigencias con su recibo y los recibos declarados.
    /// </summary>
    public async Task<ItemDetalleDto> ItemDetalleAsync(Guid empresa, Guid id, Guid itemId, ReferenciaDto empresaRef, CancellationToken ct)
    {
        await using var db = almacen.ContextoDe(empresa);
        var lote = await db.Set<Liquidacion>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false)
            ?? throw ErroresLiquidaciones.NoEncontrada();
        var item = await db.Set<LiquidacionItem>().AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.LiquidacionId == id, ct).ConfigureAwait(false)
            ?? throw ErroresLiquidaciones.ItemNoEncontrado();
        var periodos = await db.Set<LiquidacionPeriodo>().AsNoTracking().Where(p => p.ItemId == itemId).OrderBy(p => p.Periodo).ToListAsync(ct).ConfigureAwait(false);
        var recibos = await db.Set<Recibo>().AsNoTracking().Where(r => r.ItemId == itemId).OrderBy(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        var intentos = await db.Set<LiquidacionItemIntento>().AsNoTracking().Where(i => i.ItemId == itemId).OrderByDescending(i => i.Numero)
            .Select(i => new { i.Numero, i.Estado, i.ErrorCodigo, i.ErrorMensaje, i.TotalAPagar, i.RegistradoEn, i.RelanzadoPor })
            .ToListAsync(ct).ConfigureAwait(false);
        var origenes = await LotesDeOrigenAsync(db, [item], ct).ConfigureAwait(false);
        var reciboPorArchivo = recibos.GroupBy(r => r.FileIdApi, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        var b = Item(item, origenes);
        return new ItemDetalleDto(b.Id, b.LiquidacionId, b.Fila, b.Placa, b.Estado, b.ErrorCodigo, b.ErrorClase, b.ErrorMensaje, b.CampoFaltante,
            b.Departamento, b.Propietario, b.TotalAPagar, b.ResultadoStatus, b.Parcial, b.PosibleDuplicado, b.ResultadoTardio, b.RelanzadoDesde,
            b.RelanzadoDesdeItemId, b.RelanzadoDesdeLiquidacionId, b.Secretaria, empresaRef, CreadorDe(lote), lote.CreadaEn,
            item.ConsultaStatus, Resultado(item.ResultadoCifrado),
            [.. periodos.Select(p => new PeriodoDto(p.Periodo, p.Avaluo, p.Impuesto, p.Sancion, p.Intereses, p.Descuentos, p.PagosPrevios, p.TotalAPagar,
                OtrosCargos(p.OtrosCargos), p.Vencimiento, p.ReferenciaPago, p.NoLiquidable,
                p.FileIdApi is { } f && reciboPorArchivo.TryGetValue(f, out var r) ? r : null))],
            [.. recibos.Select(r => new ReciboDto(r.Id, CodigosDeEnum.Codigo(r.Tipo), r.Periodos, CodigosDeEnum.Codigo(r.Estado), r.Motivo, null))],
            item.NumeroIntento,
            [.. intentos.Select(i => new IntentoItemDto(i.Numero, CodigosDeEnum.Codigo(i.Estado), i.ErrorCodigo, i.ErrorMensaje, i.TotalAPagar, i.RegistradoEn, i.RelanzadoPor))]);
    }

    /// <summary>El ítem del contrato; <paramref name="origenes"/> da el lote de cada ítem original (relanzamientos).</summary>
    public ItemDto Item(LiquidacionItem i, IReadOnlyDictionary<Guid, Guid>? origenes = null)
    {
        ArgumentNullException.ThrowIfNull(i);
        var nombre = i.PropietarioNombreCifrado is null ? null : proteccion.DesprotegerPii(i.PropietarioNombreCifrado);
        var numero = i.DocumentoNumeroCifrado is null ? null : proteccion.DesprotegerPii(i.DocumentoNumeroCifrado);
        var propietario = nombre is null && numero is null && i.DocumentoTipo is null ? null : new PropietarioDto(nombre, i.DocumentoTipo, numero);
        var departamento = i.DepartamentoCodigo is null ? null : new DepartamentoItemDto(i.DepartamentoCodigo, i.DepartamentoNombre ?? i.DepartamentoCodigo);
        Guid? loteOrigen = i.RelanzadoDesde is { } o && origenes is not null && origenes.TryGetValue(o, out var l) ? l : null;
        return new ItemDto(i.Id, i.LiquidacionId, i.Fila, i.Placa, CodigosDeEnum.Codigo(i.Estado), i.ErrorCodigo,
            i.ErrorClase is { } c ? CodigosDeEnum.Codigo(c) : null, i.ErrorMensaje, i.CampoFaltante, departamento, propietario, i.TotalAPagar,
            i.ResultadoStatus, i.Parcial, i.PosibleDuplicado, i.ResultadoTardio, i.RelanzadoDesde, i.RelanzadoDesde, loteOrigen,
            i.SecretariaNombre is null && i.SecretariaCiudad is null ? null : new SecretariaDto(i.SecretariaNombre, i.SecretariaCiudad));
    }

    /// <summary>Motivo del cierre manual de la cabecera (columna <c>motivo_cierre</c> de la 1.2.0); null sin motivo. Es el único punto que lo lee.</summary>
    internal static string? MotivoCierreDe(Liquidacion lote)
    {
        ArgumentNullException.ThrowIfNull(lote);
        return lote.MotivoCierre;
    }

    /// <summary>Quien creó la cabecera: su id y el nombre de <c>creada_por_nombre</c> (vacío en filas anteriores a la 1.2.0).</summary>
    internal static ReferenciaDto CreadorDe(Liquidacion lote) => new(lote.CreadaPor, lote.CreadaPorNombre ?? string.Empty);

    /// <summary>Lote de cada ítem original de los relanzados de <paramref name="items"/> (join sobre <c>relanzado_desde</c>).</summary>
    private static async Task<Dictionary<Guid, Guid>> LotesDeOrigenAsync(ImpuestosDb db, IReadOnlyList<LiquidacionItem> items, CancellationToken ct)
    {
        var originales = items.Where(i => i.RelanzadoDesde is not null).Select(i => i.RelanzadoDesde!.Value).Distinct().ToList();
        if (originales.Count == 0)
            return [];
        return await db.Set<LiquidacionItem>().AsNoTracking().Where(i => originales.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.LiquidacionId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// El <c>result</c> de la consulta guardada, tal cual, con <c>file_errors</c> de la consulta si el <c>result</c> no lo trae, y sin
    /// las filas de <c>validations</c> de <see cref="CamposDeContacto"/>. Null sin resultado.
    /// </summary>
    private JsonElement? Resultado(byte[]? cifrado)
    {
        if (cifrado is null)
            return null;
        if (JsonNode.Parse(proteccion.DesprotegerResultado(cifrado)) is not JsonObject consulta || consulta["result"] is not JsonObject result)
            return null;
        if (!result.ContainsKey("file_errors"))
            result["file_errors"] = consulta["file_errors"]?.DeepClone() ?? new JsonArray();
        if (result["validations"] is JsonArray validaciones)
        {
            foreach (var v in validaciones.ToList())
            {
                if (v is JsonObject o && o["field"] is JsonValue campo && campo.TryGetValue<string>(out var nombre) && CamposDeContacto.Contains(nombre))
                    validaciones.Remove(v);
            }
        }

        return JsonSerializer.SerializeToElement(result);
    }

    private static JsonElement OtrosCargos(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// El único ítem de cada individual de <paramref name="lotes"/>: id, placa, estado y si es placa no inscrita. Solo se proyectan
    /// <c>error_codigo</c> y <c>error_mensaje</c> además de lo que ya salía; la clasificación se hace tras materializar.
    /// </summary>
    private static async Task<Dictionary<Guid, ItemIndividual>> ItemsIndividualesAsync(
        ImpuestosDb db, IReadOnlyList<Liquidacion> lotes, CancellationToken ct)
    {
        var ids = lotes.Where(l => l.Tipo == TipoLiquidacion.Individual).Select(l => l.Id).ToList();
        if (ids.Count == 0)
            return [];
        var items = await db.Set<LiquidacionItem>().AsNoTracking().Where(i => ids.Contains(i.LiquidacionId))
            .Select(i => new { i.LiquidacionId, i.Id, i.Placa, i.Estado, i.ErrorCodigo, i.ErrorMensaje }).ToListAsync(ct).ConfigureAwait(false);
        return items.GroupBy(i => i.LiquidacionId).ToDictionary(g => g.Key, g =>
        {
            var i = g.First();
            return new ItemIndividual(i.Id, i.Placa, i.Estado, PlacaNoInscrita(i.Estado, i.ErrorCodigo, i.ErrorMensaje));
        });
    }

    /// <summary>
    /// <c>placaNoInscrita</c> del resumen (1.2.3): el ítem está en <c>ERROR</c> y su error es de placa no inscrita
    /// (<see cref="ClasificacionErrores.EsPlacaNoInscrita"/>). Lo usan el grid y los últimos lotes del dashboard.
    /// </summary>
    internal static bool PlacaNoInscrita(EstadoItem estado, string? errorCodigo, string? errorMensaje) =>
        estado == EstadoItem.Error && ClasificacionErrores.EsPlacaNoInscrita(errorCodigo, errorMensaje);

    private static ResumenLiquidacionDto Resumen(Liquidacion l, ReferenciaDto empresaRef, ItemIndividual? individual)
    {
        var esIndividual = l.Tipo == TipoLiquidacion.Individual && individual is not null;
        return new ResumenLiquidacionDto(l.Id, CodigosDeEnum.Codigo(l.Tipo), CodigosDeEnum.Codigo(l.Estado), l.TotalItems, Contadores(l), l.CreadaEn,
            CreadorDe(l), empresaRef, l.NombreArchivo,
            esIndividual ? individual!.Placa : null, esIndividual ? individual!.Id : null, esIndividual ? CodigosDeEnum.Codigo(individual!.Estado) : null,
            esIndividual ? individual!.PlacaNoInscrita : null);
    }

    private static LiquidacionDto Detalle(Liquidacion l, ReferenciaDto empresaRef, ItemIndividual? individual)
    {
        var r = Resumen(l, empresaRef, individual);
        var copia = CopiaConfiguracion.DesdeJson(l.ConfigCopia);
        return new LiquidacionDto(r.Id, r.Tipo, r.Estado, r.TotalItems, r.Contadores, r.CreadaEn, r.CreadaPor, r.Empresa, r.NombreArchivo, r.Placa, r.ItemId,
            r.EstadoItem, r.PlacaNoInscrita, l.TerminadaEn, l.CerradaPor is { } p ? new ReferenciaDto(p, l.CerradaPorNombre ?? string.Empty) : null, l.CerradaEn, MotivoCierreDe(l),
            new ConfigCopiaDto(copia.MaxRegistros, copia.DepartamentosHabilitados, copia.Concurrencia));
    }

    internal static ContadoresDto Contadores(Liquidacion l) => new(
        l.CExitosas, l.CConDeuda, l.CPazYSalvo, l.CError, l.CDatosIncompletos, l.CDptoNoHabilitado, l.CIncierto, l.CSinResultado, l.CPendientes,
        l.CNoAplica);

    /// <summary>El único ítem de una individual tal como lo pinta el resumen.</summary>
    private sealed record ItemIndividual(Guid Id, string Placa, EstadoItem Estado, bool PlacaNoInscrita);
}

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
/// <c>?empresa</c> (sin <c>IgnoreQueryFilters</c>). Una liquidación de otra compañía no existe: 404 <c>LIQUIDACION_NO_ENCONTRADA</c>.
/// La PII del propietario se descifra para mostrarla completa (DA-7=b). <c>creadaPor.nombre</c> y <c>cerradaPor.nombre</c> van
/// vacíos: el servicio no guarda nombres de usuario.
/// </summary>
/// <example>
/// <code>
/// var pagina = await consulta.ListarAsync(empresa, filtros, new ReferenciaDto(empresa, "Flota Andina SAS"), ct);
/// var lote = await consulta.LiquidacionAsync(empresa, id, empresaRef, ct);
/// </code>
/// </example>
public sealed class ConsultaLiquidaciones(AlmacenCredenciales almacen, IProteccionImpuestos proteccion)
{
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

    /// <summary>La liquidación con sus ítems (respuesta 201 de la individual).</summary>
    public async Task<LiquidacionConItemsDto> ConItemsAsync(Guid empresa, Guid id, ReferenciaDto empresaRef, CancellationToken ct)
    {
        var d = await LiquidacionAsync(empresa, id, empresaRef, ct).ConfigureAwait(false);
        var items = await ItemsAsync(empresa, id, null, null, 1, 200, ct).ConfigureAwait(false);
        return new LiquidacionConItemsDto(d.Id, d.Tipo, d.Estado, d.TotalItems, d.Contadores, d.CreadaEn, d.CreadaPor, d.Empresa, d.NombreArchivo, d.Placa,
            d.ItemId, d.EstadoItem, d.TerminadaEn, d.CerradaPor, d.CerradaEn, d.ConfigCopia, items.Items);
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
        return new PaginaDto<ItemDto>([.. filas.Select(Item)], total, page, pageSize);
    }

    public ItemDto Item(LiquidacionItem i)
    {
        ArgumentNullException.ThrowIfNull(i);
        var nombre = i.PropietarioNombreCifrado is null ? null : proteccion.DesprotegerPii(i.PropietarioNombreCifrado);
        var numero = i.DocumentoNumeroCifrado is null ? null : proteccion.DesprotegerPii(i.DocumentoNumeroCifrado);
        var propietario = nombre is null && numero is null && i.DocumentoTipo is null ? null : new PropietarioDto(nombre, i.DocumentoTipo, numero);
        var departamento = i.DepartamentoCodigo is null ? null : new DepartamentoItemDto(i.DepartamentoCodigo, i.DepartamentoNombre ?? i.DepartamentoCodigo);
        return new ItemDto(i.Id, i.LiquidacionId, i.Fila, i.Placa, CodigosDeEnum.Codigo(i.Estado), i.ErrorCodigo,
            i.ErrorClase is { } c ? CodigosDeEnum.Codigo(c) : null, i.ErrorMensaje, i.CampoFaltante, departamento, propietario, i.TotalAPagar,
            i.ResultadoStatus, i.Parcial, i.PosibleDuplicado, i.ResultadoTardio, i.RelanzadoDesde,
            i.SecretariaCiudad is null ? null : new SecretariaDto(null, i.SecretariaCiudad));
    }

    private static async Task<Dictionary<Guid, (Guid Id, string Placa, EstadoItem Estado)>> ItemsIndividualesAsync(
        ImpuestosDb db, IReadOnlyList<Liquidacion> lotes, CancellationToken ct)
    {
        var ids = lotes.Where(l => l.Tipo == TipoLiquidacion.Individual).Select(l => l.Id).ToList();
        if (ids.Count == 0)
            return [];
        var items = await db.Set<LiquidacionItem>().AsNoTracking().Where(i => ids.Contains(i.LiquidacionId))
            .Select(i => new { i.LiquidacionId, i.Id, i.Placa, i.Estado }).ToListAsync(ct).ConfigureAwait(false);
        return items.GroupBy(i => i.LiquidacionId).ToDictionary(g => g.Key, g => { var i = g.First(); return (i.Id, i.Placa, i.Estado); });
    }

    private static ResumenLiquidacionDto Resumen(Liquidacion l, ReferenciaDto empresaRef, (Guid Id, string Placa, EstadoItem Estado) individual)
    {
        var esIndividual = l.Tipo == TipoLiquidacion.Individual && individual.Id != Guid.Empty;
        return new ResumenLiquidacionDto(l.Id, CodigosDeEnum.Codigo(l.Tipo), CodigosDeEnum.Codigo(l.Estado), l.TotalItems, Contadores(l), l.CreadaEn,
            new ReferenciaDto(l.CreadaPor, string.Empty), empresaRef, l.NombreArchivo,
            esIndividual ? individual.Placa : null, esIndividual ? individual.Id : null, esIndividual ? CodigosDeEnum.Codigo(individual.Estado) : null);
    }

    private static LiquidacionDto Detalle(Liquidacion l, ReferenciaDto empresaRef, (Guid Id, string Placa, EstadoItem Estado) individual)
    {
        var r = Resumen(l, empresaRef, individual);
        var copia = CopiaConfiguracion.DesdeJson(l.ConfigCopia);
        return new LiquidacionDto(r.Id, r.Tipo, r.Estado, r.TotalItems, r.Contadores, r.CreadaEn, r.CreadaPor, r.Empresa, r.NombreArchivo, r.Placa, r.ItemId,
            r.EstadoItem, l.TerminadaEn, l.CerradaPor is { } p ? new ReferenciaDto(p, string.Empty) : null, l.CerradaEn,
            new ConfigCopiaDto(copia.MaxRegistros, copia.DepartamentosHabilitados, copia.Concurrencia));
    }

    private static ContadoresDto Contadores(Liquidacion l) => new(
        l.CExitosas, l.CConDeuda, l.CPazYSalvo, l.CError, l.CDatosIncompletos, l.CDptoNoHabilitado, l.CIncierto, l.CSinResultado, l.CPendientes);
}

using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Estadisticas;
using Flit.Impuestos.Api.Modulos.Liquidaciones;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Platform.Grpc.V1;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Reportes;

/// <summary>Filtros de un reporte ya validados (los mismos en el GET y en los exportes, delta 9).</summary>
internal sealed record FiltroReporte(
    RangoDias Rango, TipoLiquidacion? TipoLiquidacion, EstadoItem? Estado, string? Departamento, Guid? Usuario, string? Placa, int Page, int PageSize);

/// <summary>Esquema <c>FilaReporteDetalle</c>; <see cref="Empresa"/> solo en la vista global (null en los demás, delta 11).</summary>
public sealed record FilaReporteDetalleDto(
    Guid LiquidacionId, Guid ItemId, string Tipo, string Placa, string Estado, string? Departamento, string? PropietarioNombre, string? DocumentoTipo,
    string? DocumentoNumero, decimal? TotalAPagar, string? ResultadoStatus, bool Parcial, DateTimeOffset CreadaEn, string Usuario, ReferenciaDto? Empresa);

/// <summary>Esquema <c>FilaReporteLotes</c>.</summary>
public sealed record FilaReporteLotesDto(
    Guid LiquidacionId, string Tipo, string Estado, string? NombreArchivo, int TotalItems, int Exitosas, int ConError, int Pendientes, DateTimeOffset CreadaEn,
    DateTimeOffset? TerminadaEn, string Usuario, ReferenciaDto? Empresa);

/// <summary>Esquema <c>FilaReporteDeuda</c>.</summary>
public sealed record FilaReporteDeudaDto(
    Guid LiquidacionId, Guid ItemId, string Placa, string? Departamento, string? PropietarioNombre, string? DocumentoTipo, string? DocumentoNumero,
    IReadOnlyList<string> Vigencias, decimal? TotalAPagar, bool Parcial, DateTimeOffset ConsultadaEn, ReferenciaDto? Empresa);

/// <summary>Esquema <c>FilaReporteErrores</c>.</summary>
public sealed record FilaReporteErroresDto(
    Guid LiquidacionId, Guid ItemId, string Placa, string Estado, string? ErrorCodigo, string? ErrorClase, string? ErrorMensaje, string? CampoFaltante,
    string? Departamento, DateTimeOffset CreadaEn, string Usuario, ReferenciaDto? Empresa);

/// <summary>Esquema <c>PaginaReporte</c>: <see cref="Items"/> lleva la fila del tipo (se serializa con su tipo real).</summary>
public sealed record PaginaReporteDto(string Tipo, IReadOnlyList<ColumnaReporteDto> Columnas, IReadOnlyList<object> Items, int Total, int Page, int PageSize);

/// <summary>
/// Lectura de los cuatro reportes (plan §5.1, F4b-CF1/CF3; impuestos.v1.yaml 1.1.0) sobre la <see cref="FuenteLiquidaciones"/> del
/// alcance. El rango y <c>usuario</c> van sobre la cabecera (fecha de creación y quién la creó), <c>tipoLiquidacion</c> sobre el tipo
/// de la cabecera, y <c>estado</c>, <c>departamento</c> y <c>placa</c> (por parte, normalizada) sobre la placa:
/// <list type="bullet">
///   <item><c>detalle</c>: todas las placas.</item>
///   <item><c>lotes</c>: las cabeceras con alguna placa que cumple los filtros de placa.</item>
///   <item><c>deuda</c>: las exitosas con <c>con_deuda</c>, con sus vigencias (las liquidables, en orden); <c>consultadaEn</c> es la
///   última actualización de la placa.</item>
///   <item><c>errores</c>: las placas en <c>ERROR</c>, <c>DATOS_INCOMPLETOS</c> o <c>DEPARTAMENTO_NO_HABILITADO</c>.</item>
/// </list>
/// Orden: la liquidación más reciente primero y, dentro de ella, por fila del archivo y placa. El propietario sale completo y
/// descifrado (DA-7=b); nunca dirección, correo ni teléfono (no se guardan). <c>usuario</c> es <c>creada_por_nombre</c> (1.2.0) o, si
/// falta, el nombre que da la plataforma (<see cref="NombresUsuarios"/>) o, si tampoco, el id. <c>departamento</c> es el nombre (o el
/// código si no hay nombre).
/// </summary>
/// <example>
/// <code>
/// var (total, filas) = await consulta.LeerAsync(fuente, alcance, definicion, filtro, paginar: true, ct);
/// </code>
/// </example>
internal sealed class ConsultaReportes(IProteccionImpuestos proteccion, NombresUsuarios nombres)
{
    /// <summary>Estados que entran en el reporte de errores.</summary>
    public static IReadOnlySet<EstadoItem> EstadosDeError { get; } = new HashSet<EstadoItem> { EstadoItem.Error, EstadoItem.DatosIncompletos, EstadoItem.DepartamentoNoHabilitado };

    public static async Task<int> ContarAsync(FuenteLiquidaciones fuente, DefinicionReporte definicion, FiltroReporte filtro, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definicion);
        return definicion.Tipo == TipoReporte.Lotes
            ? await Lotes(fuente, filtro).CountAsync(ct).ConfigureAwait(false)
            : await Placas(fuente, definicion.Tipo, filtro).CountAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Las filas del reporte (una página si <paramref name="paginar"/>; todas si no, para exportar tras el tope).</summary>
    public async Task<IReadOnlyList<object>> LeerAsync(
        FuenteLiquidaciones fuente, AlcanceLectura alcance, DefinicionReporte definicion, FiltroReporte filtro, bool paginar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fuente);
        ArgumentNullException.ThrowIfNull(alcance);
        ArgumentNullException.ThrowIfNull(definicion);
        ArgumentNullException.ThrowIfNull(filtro);
        var saltar = paginar ? (int)Math.Min((long)(filtro.Page - 1) * filtro.PageSize, int.MaxValue) : 0;
        var tomar = paginar ? filtro.PageSize : int.MaxValue;
        ReferenciaDto? Empresa(Guid id) => alcance.Global ? alcance.Referencia(id) : null;

        if (definicion.Tipo == TipoReporte.Lotes)
        {
            var lotes = await Lotes(fuente, filtro).OrderByDescending(l => l.CreadaEn).ThenByDescending(l => l.Id).Skip(saltar).Take(tomar)
                .ToListAsync(ct).ConfigureAwait(false);
            var usuarios = await nombres.ResolverAsync(lotes.Select(l => (l.TenantId, l.CreadaPor, l.CreadaPorNombre)), ct).ConfigureAwait(false);
            return [.. lotes.Select(l => new FilaReporteLotesDto(l.Id, CodigosDeEnum.Codigo(l.Tipo), CodigosDeEnum.Codigo(l.Estado), l.NombreArchivo, l.TotalItems,
                l.CExitosas, l.CError, l.CPendientes, l.CreadaEn, l.TerminadaEn, usuarios.Nombre(l.TenantId, l.CreadaPor, l.CreadaPorNombre), Empresa(l.TenantId)))];
        }

        var filas = await Placas(fuente, definicion.Tipo, filtro)
            .OrderByDescending(x => x.Lote.CreadaEn).ThenByDescending(x => x.Lote.Id).ThenBy(x => x.Item.Fila).ThenBy(x => x.Item.Placa).ThenBy(x => x.Item.Id)
            .Skip(saltar).Take(tomar)
            .Select(x => new
            {
                x.Lote.TenantId,
                LoteId = x.Lote.Id,
                x.Lote.Tipo,
                x.Lote.CreadaEn,
                x.Lote.CreadaPor,
                x.Lote.CreadaPorNombre,
                x.Item.Id,
                x.Item.Placa,
                x.Item.Estado,
                x.Item.DepartamentoCodigo,
                x.Item.DepartamentoNombre,
                x.Item.PropietarioNombreCifrado,
                x.Item.DocumentoTipo,
                x.Item.DocumentoNumeroCifrado,
                x.Item.TotalAPagar,
                x.Item.ResultadoStatus,
                x.Item.Parcial,
                x.Item.ActualizadoEn,
                x.Item.ErrorCodigo,
                x.Item.ErrorClase,
                x.Item.ErrorMensaje,
                x.Item.CampoFaltante,
            })
            .ToListAsync(ct).ConfigureAwait(false);

        string? Departamento(string? codigo, string? nombre) => string.IsNullOrWhiteSpace(nombre) ? codigo : nombre;
        string? Pii(byte[]? cifrado) => cifrado is null ? null : proteccion.DesprotegerPii(cifrado);

        switch (definicion.Tipo)
        {
            case TipoReporte.Detalle:
                {
                    var usuarios = await nombres.ResolverAsync(filas.Select(f => (f.TenantId, f.CreadaPor, f.CreadaPorNombre)), ct).ConfigureAwait(false);
                    return [.. filas.Select(f => new FilaReporteDetalleDto(f.LoteId, f.Id, CodigosDeEnum.Codigo(f.Tipo), f.Placa, CodigosDeEnum.Codigo(f.Estado),
                    Departamento(f.DepartamentoCodigo, f.DepartamentoNombre), Pii(f.PropietarioNombreCifrado), f.DocumentoTipo, Pii(f.DocumentoNumeroCifrado),
                    f.TotalAPagar, f.ResultadoStatus, f.Parcial, f.CreadaEn, usuarios.Nombre(f.TenantId, f.CreadaPor, f.CreadaPorNombre), Empresa(f.TenantId)))];
                }

            case TipoReporte.Deuda:
                {
                    var ids = filas.Select(f => f.Id).ToList();
                    var periodos = ids.Count == 0
                        ? []
                        : await fuente.Periodos.Where(p => ids.Contains(p.ItemId) && !p.NoLiquidable).Select(p => new { p.ItemId, p.Periodo })
                            .ToListAsync(ct).ConfigureAwait(false);
                    var vigencias = periodos.GroupBy(p => p.ItemId)
                        .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(p => p.Periodo).Distinct().Order(StringComparer.Ordinal)]);
                    return [.. filas.Select(f => new FilaReporteDeudaDto(f.LoteId, f.Id, f.Placa, Departamento(f.DepartamentoCodigo, f.DepartamentoNombre),
                    Pii(f.PropietarioNombreCifrado), f.DocumentoTipo, Pii(f.DocumentoNumeroCifrado), vigencias.GetValueOrDefault(f.Id) ?? [], f.TotalAPagar,
                    f.Parcial, f.ActualizadoEn, Empresa(f.TenantId)))];
                }

            default:
                {
                    var usuarios = await nombres.ResolverAsync(filas.Select(f => (f.TenantId, f.CreadaPor, f.CreadaPorNombre)), ct).ConfigureAwait(false);
                    return [.. filas.Select(f => new FilaReporteErroresDto(f.LoteId, f.Id, f.Placa, CodigosDeEnum.Codigo(f.Estado), f.ErrorCodigo,
                    f.ErrorClase is { } c ? CodigosDeEnum.Codigo(c) : null, f.ErrorMensaje, f.CampoFaltante, Departamento(f.DepartamentoCodigo, f.DepartamentoNombre),
                    f.CreadaEn, usuarios.Nombre(f.TenantId, f.CreadaPor, f.CreadaPorNombre), Empresa(f.TenantId)))];
                }
        }
    }

    private static IQueryable<Liquidacion> Cabeceras(FuenteLiquidaciones fuente, FiltroReporte f)
    {
        var inicio = f.Rango.Inicio;
        var fin = f.Rango.Fin;
        var q = fuente.Lotes.AsNoTracking().Where(l => l.CreadaEn >= inicio && l.CreadaEn < fin);
        if (f.TipoLiquidacion is { } tipo)
            q = q.Where(l => l.Tipo == tipo);
        if (f.Usuario is { } usuario)
            q = q.Where(l => l.CreadaPor == usuario);
        return q;
    }

    private static IQueryable<LiquidacionItem> FiltrarPlacas(IQueryable<LiquidacionItem> q, FiltroReporte f)
    {
        if (f.Estado is { } estado)
            q = q.Where(i => i.Estado == estado);
        if (f.Departamento is { } depto)
            q = q.Where(i => i.DepartamentoCodigo == depto);
        if (f.Placa is { } placa)
            q = q.Where(i => i.Placa.Contains(placa));
        return q;
    }

    private static IQueryable<Liquidacion> Lotes(FuenteLiquidaciones fuente, FiltroReporte f)
    {
        var q = Cabeceras(fuente, f);
        if (f.Estado is null && f.Departamento is null && f.Placa is null)
            return q;
        var placas = FiltrarPlacas(fuente.Items, f);
        return q.Where(l => placas.Any(i => i.LiquidacionId == l.Id));
    }

    private static IQueryable<PlacaConLote> Placas(FuenteLiquidaciones fuente, TipoReporte tipo, FiltroReporte f)
    {
        var items = FiltrarPlacas(fuente.Items.AsNoTracking(), f);
        if (tipo == TipoReporte.Deuda)
            items = items.Where(i => i.Estado == EstadoItem.Exitoso && i.ResultadoStatus == "con_deuda");
        else if (tipo == TipoReporte.Errores)
            items = items.Where(i => i.Estado == EstadoItem.Error || i.Estado == EstadoItem.DatosIncompletos || i.Estado == EstadoItem.DepartamentoNoHabilitado);
        return from i in items join l in Cabeceras(fuente, f) on i.LiquidacionId equals l.Id select new PlacaConLote { Item = i, Lote = l };
    }

    private sealed class PlacaConLote
    {
        public required LiquidacionItem Item { get; init; }

        public required Liquidacion Lote { get; init; }
    }
}

/// <summary>Nombres resueltos de los creadores de una lectura (<see cref="NombresUsuarios.ResolverAsync"/>).</summary>
internal sealed class NombresResueltos(IReadOnlyDictionary<(Guid Empresa, Guid Usuario), string> plataforma)
{
    /// <summary><c>creada_por_nombre</c> si lo hay; si no, el de la plataforma; si tampoco, el id.</summary>
    public string Nombre(Guid empresa, Guid usuario, string? delToken) =>
        !string.IsNullOrWhiteSpace(delToken) ? delToken
        : plataforma.TryGetValue((empresa, usuario), out var nombre) && !string.IsNullOrWhiteSpace(nombre) ? nombre
        : usuario.ToString();
}

/// <summary>
/// Nombre de los usuarios que crearon liquidaciones, para la columna «Usuario» y el catálogo de filtros (M14). Primero
/// <c>creada_por_nombre</c> (1.2.0, el nombre del token al crear); para las filas sin él, la lista de usuarios de Impuestos de la
/// compañía en la plataforma (<see cref="IUsuariosPlataforma"/>, incluidos los suspendidos), una sola vez por compañía y petición;
/// si la plataforma no responde o niega la lista, el id (el reporte no falla y el motivo queda en el log, sin datos personales).
/// </summary>
/// <example>
/// <code>
/// var resueltos = await nombres.ResolverAsync(lotes.Select(l => (l.TenantId, l.CreadaPor, l.CreadaPorNombre)), ct);
/// var usuario = resueltos.Nombre(empresa, creador, lote.CreadaPorNombre);
/// </code>
/// </example>
internal sealed partial class NombresUsuarios(IUsuariosPlataforma plataforma, UsuarioActual actual, ILogger<NombresUsuarios> logger)
{
    /// <summary>Páginas de la plataforma por compañía como máximo (100 usuarios cada una).</summary>
    public const int PaginasMaximas = 20;

    private readonly Dictionary<Guid, IReadOnlyDictionary<Guid, string>> _porEmpresa = [];

    public async Task<NombresResueltos> ResolverAsync(IEnumerable<(Guid Empresa, Guid Usuario, string? DelToken)> creadores, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(creadores);
        var faltan = creadores.Where(c => string.IsNullOrWhiteSpace(c.DelToken)).Select(c => c.Empresa).Distinct().ToList();
        var nombres = new Dictionary<(Guid, Guid), string>();
        foreach (var empresa in faltan)
        {
            foreach (var (usuario, nombre) in await DeEmpresaAsync(empresa, ct).ConfigureAwait(false))
                nombres[(empresa, usuario)] = nombre;
        }

        return new NombresResueltos(nombres);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> DeEmpresaAsync(Guid empresa, CancellationToken ct)
    {
        if (_porEmpresa.TryGetValue(empresa, out var previos))
            return previos;
        var nombres = new Dictionary<Guid, string>();
        try
        {
            var contexto = new ContextoUsuarios(empresa, actual.Id);
            for (var pagina = 1; pagina <= PaginasMaximas; pagina++)
            {
                var r = await plataforma.ListarAsync(contexto, new ListarUsuariosProductoRequest { Pagina = pagina, Tamano = Usuarios.UsuariosEndpoints.TamanoPlataforma }, ct)
                    .ConfigureAwait(false);
                foreach (var u in r.Usuarios)
                {
                    if (u.Tipo == TipoFila.Usuario && Guid.TryParse(u.Id, out var id) && !string.IsNullOrWhiteSpace(u.Nombre))
                        nombres[id] = u.Nombre.Trim();
                }

                if (r.Usuarios.Count < Usuarios.UsuariosEndpoints.TamanoPlataforma || pagina * Usuarios.UsuariosEndpoints.TamanoPlataforma >= r.Total)
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogSinNombres(logger, ex.GetType().Name);
        }

        _porEmpresa[empresa] = nombres;
        return nombres;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudieron leer los nombres de usuarios de la plataforma ({Motivo}): los reportes muestran el id.")]
    private static partial void LogSinNombres(ILogger logger, string motivo);
}

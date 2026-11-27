using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Modulos.Liquidaciones;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Microsoft.Extensions.Primitives;

namespace Flit.Impuestos.Api.Modulos.Estadisticas;

/// <summary>
/// Qué compañías lee una petición del dashboard, de los reportes o de sus catálogos (M14): una sola (la del token, o la que el Super
/// Admin eligió con <c>?empresa</c>) o, para el Super Admin sin <c>empresa</c>, la vista global con todas las que tienen o tuvieron
/// Impuestos (incluidas las suspendidas, F4-CF10). <see cref="Referencias"/> da el <c>{id, nombre}</c> de cada una.
/// </summary>
internal sealed record AlcanceLectura(IReadOnlyList<Guid> Empresas, bool Global, IReadOnlyDictionary<Guid, ReferenciaDto> Referencias)
{
    public ReferenciaDto Referencia(Guid empresa) =>
        Referencias.TryGetValue(empresa, out var r) ? r : new ReferenciaDto(empresa, string.Empty);
}

/// <summary>
/// Las tablas de liquidaciones del <see cref="AlcanceLectura"/>: con una compañía, un contexto con esa compañía fijada (el filtro
/// por empresa sigue puesto, sin <c>IgnoreQueryFilters</c>); en la vista global, las consultas de <see cref="LecturaEntreEmpresas"/>
/// acotadas a las compañías auditadas. <see cref="Db"/> sirve para las tablas globales (caché de <c>/fuentes</c>).
/// </summary>
internal sealed class FuenteLiquidaciones : IAsyncDisposable
{
    private readonly ImpuestosDb? _propio;

    private FuenteLiquidaciones(ImpuestosDb db, IQueryable<Liquidacion> lotes, IQueryable<LiquidacionItem> items, IQueryable<LiquidacionPeriodo> periodos, bool propio)
    {
        Db = db;
        Lotes = lotes;
        Items = items;
        Periodos = periodos;
        _propio = propio ? db : null;
    }

    public ImpuestosDb Db { get; }

    public IQueryable<Liquidacion> Lotes { get; }

    public IQueryable<LiquidacionItem> Items { get; }

    public IQueryable<LiquidacionPeriodo> Periodos { get; }

    public static FuenteLiquidaciones Abrir(AlcanceLectura alcance, AlmacenCredenciales almacen, LecturaEntreEmpresas lectura)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        ArgumentNullException.ThrowIfNull(almacen);
        ArgumentNullException.ThrowIfNull(lectura);
        if (alcance.Global)
            return new FuenteLiquidaciones(lectura.Contexto, lectura.LiquidacionesDe(alcance.Empresas), lectura.ItemsDe(alcance.Empresas), lectura.PeriodosDe(alcance.Empresas), false);
        var db = almacen.ContextoDe(alcance.Empresas[0]);
        return new FuenteLiquidaciones(db, db.Set<Liquidacion>().AsQueryable(), db.Set<LiquidacionItem>().AsQueryable(), db.Set<LiquidacionPeriodo>().AsQueryable(), true);
    }

    public ValueTask DisposeAsync() => _propio?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>
/// Resuelve el <see cref="AlcanceLectura"/> con la regla única de <c>?empresa</c> (impuestos.v1.yaml 1.1.0, delta 8):
/// <list type="bullet">
///   <item>Quien no es Super Admin y envía <c>empresa</c>: 403 <c>EMPRESA_SOLO_SUPER_ADMIN</c> (antes que cualquier 400).</item>
///   <item>Super Admin sin <c>empresa</c>: vista global; cada compañía queda auditada (<see cref="AuditoriaLecturaBackoffice"/>)
///   antes de leer, con el recurso de la ruta.</item>
///   <item>Super Admin con <c>empresa</c>: no uuid o repetida, 400 <c>EMPRESA_REQUERIDA</c>; sin Impuestos, 404
///   <c>EMPRESA_NO_ENCONTRADA</c>; si existe, se audita y se lee solo esa.</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// FiltroEstadisticas.ValidarEmpresa(usuario, query["empresa"]);          // primero: 403 antes que el 400 del rango
/// var rango = FiltroEstadisticas.Rango(query, reloj);
/// var alcance = await filtro.ResolverAsync(query["empresa"], "dashboard", ct);
/// </code>
/// </example>
internal sealed class FiltroEstadisticas(
    UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito, AuditoriaLecturaBackoffice auditoria, LecturaEntreEmpresas lectura)
{
    /// <summary>¿Es la vista global (Super Admin sin <c>empresa</c>)?</summary>
    public static bool EsGlobal(UsuarioActual usuario, StringValues empresa)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return usuario.EsSuperAdmin && StringValues.IsNullOrEmpty(empresa);
    }

    /// <summary>La regla de <c>?empresa</c> sin leer nada: 403 o 400 antes de validar el resto de la consulta.</summary>
    public static void ValidarEmpresa(UsuarioActual usuario, StringValues empresa)
    {
        if (!EsGlobal(usuario, empresa))
            EmpresaExplicita.Opcional(usuario.EsSuperAdmin, empresa);
    }

    /// <summary><c>desde</c>/<c>hasta</c> (30 días por defecto) con el 400 y el literal del contrato.</summary>
    public static RangoDias Rango(IQueryCollection query, TimeProvider reloj) => RangoDias.Leer(query, RangoDias.Hoy(reloj));

    public async Task<AlcanceLectura> ResolverAsync(StringValues empresa, string recurso, CancellationToken ct)
    {
        if (EsGlobal(usuario, empresa))
        {
            var filas = await lectura.AprovisionamientosAsync(ct).ConfigureAwait(false);
            var directorio = await ambito.DirectorioAsync(ct).ConfigureAwait(false);
            var ids = filas.Select(f => f.TenantId).Distinct().ToList();
            foreach (var id in ids)
                await auditoria.RegistrarAsync(id, recurso, ct).ConfigureAwait(false);
            var referencias = ids.ToDictionary(id => id, id =>
            {
                var e = AmbitoDeLaEmpresa.Identificar(directorio, id);
                return new ReferenciaDto(e.Id, e.Nombre);
            });
            return new AlcanceLectura(ids, true, referencias);
        }

        var (elegida, referencia) = await LiquidacionesEndpoints.EmpresaDeEscrituraAsync(empresa, usuario, configuracion, ambito, ct).ConfigureAwait(false);
        if (usuario.EsSuperAdmin)
            await auditoria.RegistrarAsync(elegida, recurso, ct).ConfigureAwait(false);
        return new AlcanceLectura([elegida], false, new Dictionary<Guid, ReferenciaDto> { [elegida] = referencia });
    }
}

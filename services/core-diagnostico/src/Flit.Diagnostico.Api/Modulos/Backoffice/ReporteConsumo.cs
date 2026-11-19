using Flit.Diagnostico.Api.Modulos.Estadisticas;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Derivacion;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Las compañías de la petición del backoffice con su nombre y NIT de Identidad (HU #14579, BE-4), para el reporte <c>consumo</c>. La
/// ruta del backoffice la llena antes de llamar al motor con la lista que ya leyó (una sola llamada a Identidad por petición, SP-4).
/// </summary>
internal sealed class EmpresasDelReporte
{
    public IReadOnlyDictionary<Guid, EmpresaPlataforma> Empresas { get; set; } = new Dictionary<Guid, EmpresaPlataforma>();
}

/// <summary>
/// Reporte <c>consumo</c> (HU #14579, BE-4; contrato-api §4), solo del backoffice: una fila por compañía habilitada con datos en el rango
/// (al menos un lote, un saneamiento o una operación de derivación), ordenadas por nombre. Lotes, vehículos y consultas por la cohorte de
/// lotes creados en el rango (criterio S1); saneamientos por <c>solicitado_en</c>; trámites creados = ítems CREADO de las operaciones
/// creadas en el rango. Las consultas cuentan solo lo que consume según el contrato (supuesto O11, CR-BE-4): celdas <c>COMPLETADO</c> y
/// <c>ERROR</c> que no se reutilizaron (D-10); ni <c>NA</c>, ni <c>PENDIENTE</c>/<c>EN_CURSO</c>, ni las reutilizadas del mismo día. Las
/// tres fuentes RUNT y las tres SIMIT se suman. Sin PII: el NIT es de una persona jurídica y nunca va al nombre del archivo. Lee de las
/// fuentes ya acotadas por el backoffice. Vive en <c>Modulos/Backoffice</c> porque solo existe allí (CR-BE-9). Es scoped: el conteo y la
/// lectura de la misma petición reutilizan un solo agregado (CR-BE-8).
/// </summary>
internal sealed class ReporteConsumo(EmpresasDelReporte empresas) : IReporte
{
    private static readonly Fuente[] Runt = [Fuente.Runt, Fuente.RuntPropietario, Fuente.RuntLocatario];
    private static readonly Fuente[] Simit = [Fuente.SimitVehiculo, Fuente.SimitPropietario, Fuente.SimitLocatario];

    private (ContextoReporte Contexto, FiltroReporte Filtro, List<FilaReporte> Filas)? _agregado;

    public TipoReporte Tipo => TipoReporte.Consumo;

    public async Task<int> ContarAsync(ContextoReporte contexto, FiltroReporte filtro, CancellationToken ct) =>
        (await FilasAsync(contexto, filtro, ct).ConfigureAwait(false)).Count;

    public async Task<IReadOnlyList<FilaReporte>> LeerAsync(ContextoReporte contexto, FiltroReporte filtro, int saltar, int? tomar, CancellationToken ct)
    {
        var filas = (await FilasAsync(contexto, filtro, ct).ConfigureAwait(false)).Skip(saltar);
        return [.. tomar is { } t ? filas.Take(t) : filas];
    }

    /// <summary>El agregado del contexto y el filtro de la petición; si ya se calculó para los mismos objetos, se reutiliza.</summary>
    private async Task<List<FilaReporte>> FilasAsync(ContextoReporte contexto, FiltroReporte filtro, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(filtro);
        if (_agregado is { } previo && ReferenceEquals(previo.Contexto, contexto) && ReferenceEquals(previo.Filtro, filtro))
            return previo.Filas;

        var filas = await AgregarAsync(contexto, filtro, ct).ConfigureAwait(false);
        _agregado = (contexto, filtro, filas);
        return filas;
    }

    private async Task<List<FilaReporte>> AgregarAsync(ContextoReporte contexto, FiltroReporte filtro, CancellationToken ct)
    {
        var f = contexto.Fuentes;
        var (inicio, fin) = (filtro.DesdeInstante, filtro.HastaInstante);
        var cohorte = f.Cabeceras.Where(c => c.CreadoEn >= inicio && c.CreadoEn < fin);

        var lotes = await cohorte.GroupBy(c => c.TenantId).Select(g => new { g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Cantidad, ct).ConfigureAwait(false);
        var vehiculos = await (from v in f.Vehiculos
                               join c in cohorte on new { v.TenantId, Id = v.DiagnosticoId } equals new { c.TenantId, c.Id }
                               group v by v.TenantId into g
                               select new { g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Cantidad, ct).ConfigureAwait(false);
        var celdas = await (from x in f.Consultas.Where(x => (x.Estado == EstadoCelda.Completado || x.Estado == EstadoCelda.Error) && x.ReutilizadaDeId == null)
                            join c in cohorte on new { x.TenantId, Id = x.DiagnosticoId } equals new { c.TenantId, c.Id }
                            group x by new { x.TenantId, x.Fuente, x.Estado } into g
                            select new { g.Key.TenantId, g.Key.Fuente, g.Key.Estado, Cantidad = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        var saneamientos = await f.Saneamientos.Where(s => s.SolicitadoEn >= inicio && s.SolicitadoEn < fin)
            .GroupBy(s => s.TenantId).Select(g => new { g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Cantidad, ct).ConfigureAwait(false);
        var operaciones = f.Operaciones.Where(o => o.CreadaEn >= inicio && o.CreadaEn < fin);
        var conOperaciones = await operaciones.Select(o => o.TenantId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var tramites = await (from i in f.Derivaciones
                              join o in operaciones on new { i.TenantId, Id = i.OperacionId } equals new { o.TenantId, o.Id }
                              where i.Estado == EstadoItemDerivacion.Creado
                              group i by i.TenantId into g
                              select new { g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Cantidad, ct).ConfigureAwait(false);

        var conDatos = lotes.Keys.Concat(saneamientos.Keys).Concat(conOperaciones).Distinct()
            .Where(empresas.Empresas.ContainsKey)
            .Select(e => empresas.Empresas[e])
            .OrderBy(e => EmpresasBackofficeEndpoints.ClaveOrden(e.Nombre), StringComparer.Ordinal)
            .ThenBy(e => e.Id);
        return [.. conDatos.Select(e =>
        {
            int Celdas(Func<Fuente, bool> fuente, Func<EstadoCelda, bool> estado) =>
                celdas.Where(c => c.TenantId == e.Id && fuente(c.Fuente) && estado(c.Estado)).Sum(c => c.Cantidad);
            var exitosas = Celdas(_ => true, s => s == EstadoCelda.Completado);
            var conError = Celdas(_ => true, s => s == EstadoCelda.Error);
            var fila = new FilaReporte { Empresa = e.Id };
            var v = fila.Valores;
            v[DefinicionReporte.EmpresaNombre] = e.Nombre;
            v["empresa_nit"] = e.Nit;
            v["diagnosticos"] = lotes.GetValueOrDefault(e.Id);
            v["vehiculos"] = vehiculos.GetValueOrDefault(e.Id);
            v["consultas_exitosas"] = exitosas;
            v["consultas_con_error"] = conError;
            v["tasa_exito"] = CalculadoraEstadisticas.TasaExito(exitosas, conError);
            v["consultas_runt"] = Celdas(Runt.Contains, _ => true);
            v["consultas_simit"] = Celdas(Simit.Contains, _ => true);
            v["consultas_avaluo"] = Celdas(x => x == Fuente.Avaluo, _ => true);
            v["consultas_impuesto"] = Celdas(x => x == Fuente.ImpuestoDepartamental, _ => true);
            v["saneamientos"] = saneamientos.GetValueOrDefault(e.Id);
            v["tramites_creados"] = tramites.GetValueOrDefault(e.Id);
            return fila;
        })];
    }
}

using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Saneamiento;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Estadisticas;

/// <summary>Un lote de la cohorte: empresa, instante de creación, modalidad, tipo de análisis y cuántos vehículos tiene.</summary>
internal sealed record LoteLeido(Guid Empresa, DateTimeOffset CreadoEn, bool EsExpress, ModoDiagnostico Modo, int Vehiculos);

/// <summary>Celdas de la cohorte por fuente y estado.</summary>
internal sealed record ConteoCelda(Fuente Fuente, EstadoCelda Estado, int Cantidad);

/// <summary>Suma de duraciones (ms) e intentos no MANUAL de la cohorte por fuente.</summary>
internal sealed record TiempoFuente(Fuente Fuente, double SumaMs, int Intentos);

/// <summary>Vehículos de la cohorte por veredicto del dictamen vigente (null = sin dictamen).</summary>
internal sealed record ConteoVeredicto(string? Veredicto, int Cantidad);

/// <summary>Saneamientos del rango (por <c>solicitado_en</c>) por empresa y estado.</summary>
internal sealed record ConteoSaneamiento(Guid Empresa, EstadoSaneamiento Estado, int Cantidad);

/// <summary>Contadores de las operaciones de derivación del rango (por <c>creada_en</c>) por empresa.</summary>
internal sealed record ConteoOperaciones(Guid Empresa, int Creadas, int Omitidas, int Fallidas);

/// <summary>Ítems de esas operaciones por estado.</summary>
internal sealed record ConteoItems(EstadoItemDerivacion Estado, int Cantidad);

/// <summary>Lo que la base agregó para un ámbito y un rango; el DTO se arma en memoria con <see cref="CalculadoraEstadisticas.Armar"/>.</summary>
internal sealed record LecturasEstadisticas(
    IReadOnlyList<LoteLeido> Lotes, int VehiculosUnicos, IReadOnlyList<ConteoCelda> Celdas, IReadOnlyList<TiempoFuente> Tiempos,
    IReadOnlyList<ConteoVeredicto> Veredictos, IReadOnlyList<ConteoSaneamiento> Saneamientos, IReadOnlyList<ConteoOperaciones> Operaciones,
    IReadOnlyList<ConteoItems> Items)
{
    /// <summary>Empresas que aportan al menos un lote, un saneamiento o una derivación al rango (auditoría de E2, SP-7).</summary>
    public IReadOnlySet<Guid> EmpresasConDatos =>
        Lotes.Select(l => l.Empresa).Concat(Saneamientos.Select(s => s.Empresa)).Concat(Operaciones.Select(o => o.Empresa)).ToHashSet();
}

/// <summary>
/// Calculadora de E1–E3 (HU #14579, BE-1; contrato-api §1, criterio S1): la misma para la compañía y para el backoffice, sobre
/// <see cref="FuentesEstadisticas"/> ya acotadas. <see cref="LeerAsync"/> hace ocho consultas agregadas sin seguimiento (nada de filas de
/// vehículos ni de PII sale de la base: solo conteos, salvo un renglón por lote de la cohorte con su conteo de vehículos);
/// <see cref="Armar"/> arma el DTO en memoria (serie con ceros, los 5 estados de saneamiento, fuentes en el orden del enum).
/// </summary>
internal static class CalculadoraEstadisticas
{
    private static readonly EstadoSaneamiento[] EstadosSaneamiento = Enum.GetValues<EstadoSaneamiento>();

    public static async Task<LecturasEstadisticas> LeerAsync(FuentesEstadisticas fuentes, FiltroEstadisticas filtro, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fuentes);
        ArgumentNullException.ThrowIfNull(filtro);
        var (inicio, fin) = (filtro.Inicio, filtro.Fin);
        var visibles = fuentes.Cabeceras;
        var cohorte = visibles.Where(c => c.CreadoEn >= inicio && c.CreadoEn < fin);
        var vehiculosTodos = fuentes.Vehiculos;

        var lotes = await cohorte
            .Select(c => new LoteLeido(c.TenantId, c.CreadoEn, c.EsExpress, c.Modo,
                vehiculosTodos.Count(v => v.TenantId == c.TenantId && v.DiagnosticoId == c.Id)))
            .ToListAsync(ct).ConfigureAwait(false);

        var vehiculos = from v in fuentes.Vehiculos
                        join c in cohorte on new { v.TenantId, Id = v.DiagnosticoId } equals new { c.TenantId, c.Id }
                        select v;
        var unicos = await vehiculos.Select(v => new { v.TenantId, v.IdentificadorNorm }).Distinct().CountAsync(ct).ConfigureAwait(false);

        var celdas = await (from x in fuentes.Celdas
                            join c in cohorte on new { x.TenantId, Id = x.DiagnosticoId } equals new { c.TenantId, c.Id }
                            group x by new { x.Fuente, x.Estado } into g
                            select new ConteoCelda(g.Key.Fuente, g.Key.Estado, g.Count()))
            .ToListAsync(ct).ConfigureAwait(false);

        var tiempos = await (from i in fuentes.Intentos
                             join c in cohorte on new { i.TenantId, Id = i.DiagnosticoId } equals new { c.TenantId, c.Id }
                             where i.Disparador != DisparadorIntento.Manual
                             group i by i.Fuente into g
                             select new TiempoFuente(g.Key, g.Sum(i => (i.Fin - i.Inicio).TotalMilliseconds), g.Count()))
            .ToListAsync(ct).ConfigureAwait(false);

        var dictamenes = fuentes.Dictamenes.Where(d => d.Vigente);
        var veredictos = await (from v in vehiculos
                                join d in dictamenes on new { v.TenantId, VehiculoId = v.Id } equals new { d.TenantId, d.VehiculoId } into dj
                                from d in dj.DefaultIfEmpty()
                                select new { Veredicto = d == null ? null : d.Veredicto } into fila
                                group fila by fila.Veredicto into g
                                select new ConteoVeredicto(g.Key, g.Count()))
            .ToListAsync(ct).ConfigureAwait(false);

        var saneamientos = await fuentes.Saneamientos
            .Where(s => s.SolicitadoEn >= inicio && s.SolicitadoEn < fin && visibles.Any(c => c.TenantId == s.TenantId && c.Id == s.DiagnosticoId))
            .GroupBy(s => new { s.TenantId, s.Estado })
            .Select(g => new ConteoSaneamiento(g.Key.TenantId, g.Key.Estado, g.Count()))
            .ToListAsync(ct).ConfigureAwait(false);

        var operaciones = fuentes.Operaciones
            .Where(o => o.CreadaEn >= inicio && o.CreadaEn < fin && visibles.Any(c => c.TenantId == o.TenantId && c.Id == o.DiagnosticoId));
        var porEmpresa = await operaciones
            .GroupBy(o => o.TenantId)
            .Select(g => new ConteoOperaciones(g.Key, g.Sum(o => o.Creadas), g.Sum(o => o.Omitidas), g.Sum(o => o.Fallidas)))
            .ToListAsync(ct).ConfigureAwait(false);
        var items = await (from i in fuentes.Items
                           join o in operaciones on new { i.TenantId, Id = i.OperacionId } equals new { o.TenantId, o.Id }
                           group i by i.Estado into g
                           select new ConteoItems(g.Key, g.Count()))
            .ToListAsync(ct).ConfigureAwait(false);

        return new LecturasEstadisticas(lotes, unicos, celdas, tiempos, veredictos, saneamientos, porEmpresa, items);
    }

    public static EstadisticasDto Armar(
        AmbitoEstadisticasDto ambito, FiltroEstadisticas filtro, DateTimeOffset generadoEn, LecturasEstadisticas lecturas, IReadOnlyList<TopEmpresaDto> top)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        ArgumentNullException.ThrowIfNull(lecturas);
        var granularidad = Cubetas.De(filtro.Dias);
        var lotes = lecturas.Lotes;
        var porCubeta = lotes.ToLookup(l => Cubetas.Inicio(granularidad, FiltroEstadisticas.DiaDe(l.CreadoEn)));
        var serie = Cubetas.Del(granularidad, filtro.Desde, filtro.Hasta).Select(inicio =>
        {
            var deLaCubeta = porCubeta[inicio].ToList();
            return new CubetaEstadisticasDto(inicio, deLaCubeta.Count, deLaCubeta.Count(l => l.EsExpress), deLaCubeta.Count(l => !l.EsExpress), deLaCubeta.Sum(l => l.Vehiculos));
        }).ToList();

        int Celdas(Func<ConteoCelda, bool> condicion) => lecturas.Celdas.Where(condicion).Sum(c => c.Cantidad);
        var exitosas = Celdas(c => c.Estado == EstadoCelda.Completado);
        var conError = Celdas(c => c.Estado == EstadoCelda.Error);
        var intentos = lecturas.Tiempos.Sum(t => t.Intentos);
        var items = lecturas.Items.ToDictionary(i => i.Estado, i => i.Cantidad);
        int Items(EstadoItemDerivacion estado) => items.GetValueOrDefault(estado);

        var resumen = new ResumenEstadisticasDto(
            Diagnosticos: lotes.Count,
            DiagnosticosExpress: lotes.Count(l => l.EsExpress),
            DiagnosticosMasivos: lotes.Count(l => !l.EsExpress),
            Vehiculos: lotes.Sum(l => l.Vehiculos),
            VehiculosUnicos: lecturas.VehiculosUnicos,
            Consultas: Celdas(_ => true),
            ConsultasExitosas: exitosas,
            ConsultasConError: conError,
            ConsultasNoAplica: Celdas(c => c.Estado == EstadoCelda.Na),
            ConsultasPendientes: Celdas(c => c.Estado is EstadoCelda.Pendiente or EstadoCelda.EnCurso),
            TasaExito: TasaExito(exitosas, conError),
            TiempoPromedioConsultaMs: Promedio(lecturas.Tiempos.Sum(t => t.SumaMs), intentos),
            SaneamientosSolicitados: lecturas.Saneamientos.Sum(s => s.Cantidad),
            TramitesCreados: Items(EstadoItemDerivacion.Creado));

        var fuentes = Enum.GetValues<Fuente>()
            .Select(f => (Fuente: f, Celdas: lecturas.Celdas.Where(c => c.Fuente == f).ToList()))
            .Where(f => f.Celdas.Sum(c => c.Cantidad) > 0)
            .Select(f =>
            {
                int De(params EstadoCelda[] estados) => f.Celdas.Where(c => estados.Contains(c.Estado)).Sum(c => c.Cantidad);
                var tiempo = lecturas.Tiempos.Where(t => t.Fuente == f.Fuente).ToList();
                return new FuenteEstadisticasDto(
                    CodigosDeEnum.Codigo(f.Fuente), f.Celdas.Sum(c => c.Cantidad), De(EstadoCelda.Completado), De(EstadoCelda.Error), De(EstadoCelda.Na),
                    De(EstadoCelda.Pendiente, EstadoCelda.EnCurso), TasaExito(De(EstadoCelda.Completado), De(EstadoCelda.Error)),
                    Promedio(tiempo.Sum(t => t.SumaMs), tiempo.Sum(t => t.Intentos)));
            })
            .ToList();

        int Veredicto(string? codigo) => lecturas.Veredictos.Where(v => v.Veredicto == codigo).Sum(v => v.Cantidad);

        return new EstadisticasDto(
            ambito,
            new PeriodoEstadisticasDto(filtro.Desde, filtro.Hasta, CodigosDeEnum.Codigo(granularidad)),
            generadoEn.ToOffset(Domain.Consultas.VigenciaDelDia.OffsetColombia),
            resumen,
            serie,
            new VeredictosDto(Veredicto("VIABLE"), Veredicto("BLOQUEADO"), Veredicto(null)),
            new TiposAnalisisDto(lotes.Count(l => l.Modo == ModoDiagnostico.Simple), lotes.Count(l => l.Modo == ModoDiagnostico.ConAnalisis)),
            fuentes,
            [.. EstadosSaneamiento.Select(e => new EstadoCantidadDto(CodigosDeEnum.Codigo(e), lecturas.Saneamientos.Where(s => s.Estado == e).Sum(s => s.Cantidad)))],
            new DerivacionesEstadisticasDto(
                lecturas.Operaciones.Sum(o => o.Creadas), lecturas.Operaciones.Sum(o => o.Omitidas), lecturas.Operaciones.Sum(o => o.Fallidas),
                Items(EstadoItemDerivacion.Pendiente) + Items(EstadoItemDerivacion.Enviado)),
            top);
    }

    /// <summary>
    /// Hasta 10 compañías por diagnósticos desc, vehículos desc y nombre; solo las de <paramref name="nombres"/> (las habilitadas de
    /// Identidad), con su nombre, que nunca es null.
    /// </summary>
    public static IReadOnlyList<TopEmpresaDto> Top(LecturasEstadisticas lecturas, IReadOnlyDictionary<Guid, string> nombres)
    {
        ArgumentNullException.ThrowIfNull(lecturas);
        ArgumentNullException.ThrowIfNull(nombres);
        return lecturas.Lotes
            .Where(l => nombres.ContainsKey(l.Empresa))
            .GroupBy(l => l.Empresa)
            .Select(g => new TopEmpresaDto(g.Key, nombres[g.Key], g.Count(), g.Sum(l => l.Vehiculos)))
            .OrderByDescending(t => t.Diagnosticos)
            .ThenByDescending(t => t.Vehiculos)
            .ThenBy(t => t.EmpresaNombre, StringComparer.Ordinal)
            .ThenBy(t => t.EmpresaId)
            .Take(10)
            .ToList();
    }

    /// <summary><c>exitosas / (exitosas + con_error)</c> con 4 decimales; null si el divisor es 0.</summary>
    public static double? TasaExito(int exitosas, int conError) =>
        exitosas + conError == 0 ? null : Math.Round((double)exitosas / (exitosas + conError), 4, MidpointRounding.AwayFromZero);

    private static long? Promedio(double sumaMs, int intentos) =>
        intentos == 0 ? null : (long)Math.Round(sumaMs / intentos, MidpointRounding.AwayFromZero);
}

using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Reporte <c>consultas</c> (HU #14579, BE-2; contrato-api §4; SP-16): un renglón por celda de los diagnósticos creados en el rango. En
/// la compañía no hay rastro del soporte FLIT: sin la columna <c>origen</c> y sin contar los intentos <c>MANUAL</c> (ni en
/// <c>intentos</c> ni en <c>duracion_ms</c>); en el backoffice salen los dos.
/// </summary>
internal sealed class ReporteConsultas : IReporte
{
    public TipoReporte Tipo => TipoReporte.Consultas;

    public Task<int> ContarAsync(ContextoReporte contexto, FiltroReporte filtro, CancellationToken ct) => Consulta(contexto, filtro).CountAsync(ct);

    public async Task<IReadOnlyList<FilaReporte>> LeerAsync(ContextoReporte contexto, FiltroReporte filtro, int saltar, int? tomar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var consulta = Consulta(contexto, filtro).OrderByDescending(f => f.CreadoEn).ThenBy(f => f.Id).Skip(saltar);
        if (tomar is { } t)
            consulta = consulta.Take(t);
        var leidas = await consulta.ToListAsync(ct).ConfigureAwait(false);

        var intentos = new List<(Guid Empresa, Guid ConsultaId, DateTimeOffset Inicio, DateTimeOffset Fin)>();
        var conSoporte = contexto.ConSoporte;
        foreach (var lote in ValoresReporte.Lotes(leidas))
        {
            var ids = lote.Select(l => l.Id).ToList();
            var empresas = lote.Select(l => l.Empresa).Distinct().ToList();
            var filas = await contexto.Fuentes.Intentos
                .Where(i => empresas.Contains(i.TenantId) && ids.Contains(i.ConsultaId) && (conSoporte || i.Disparador != DisparadorIntento.Manual))
                .Select(i => new { i.TenantId, i.ConsultaId, i.Inicio, i.Fin })
                .ToListAsync(ct).ConfigureAwait(false);
            intentos.AddRange(filas.Select(i => (i.TenantId, i.ConsultaId, i.Inicio, i.Fin)));
        }

        var porCelda = intentos.ToLookup(i => (i.Empresa, i.ConsultaId));
        return [.. leidas.Select(l =>
        {
            var propios = porCelda[(l.Empresa, l.Id)].ToList();
            var fila = new FilaReporte { Empresa = l.Empresa };
            var v = fila.Valores;
            v["fecha_diagnostico"] = ValoresReporte.Instante(l.CreadoEn);
            v["diagnostico_nombre"] = l.DiagnosticoNombre;
            v["placa"] = l.Placa;
            v["fuente"] = ValoresReporte.Codigo<Fuente>(l.Fuente);
            v["estado"] = ValoresReporte.Codigo<EstadoCelda>(l.Estado);
            v["motivo_na"] = ValoresReporte.Codigo(l.MotivoNa);
            v["tipo_error"] = l.TipoError;
            v["intentos"] = propios.Count;
            v["duracion_ms"] = propios.Count == 0 ? null : (long)propios.Sum(i => (i.Fin - i.Inicio).TotalMilliseconds);
            v["dato_obtenido_en"] = ValoresReporte.Instante(l.DatoObtenidoEn);
            v["reutilizada"] = l.Reutilizada ? "SI" : "NO";
            if (conSoporte)
                v[DefinicionReporte.Origen] = ValoresReporte.Codigo(l.Origen);
            return fila;
        })];
    }

    private static IQueryable<FilaLeida> Consulta(ContextoReporte contexto, FiltroReporte filtro)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(filtro);
        var f = contexto.Fuentes;
        var (desde, hasta) = (filtro.DesdeInstante, filtro.HastaInstante);
        var cabeceras = f.Cabeceras.Where(c => c.CreadoEn >= desde && c.CreadoEn < hasta);
        var celdas = f.Consultas;
        if (filtro.Fuentes() is { Count: > 0 } fuentes)
            celdas = celdas.Where(c => fuentes.Contains(c.Fuente));
        if (filtro.Estados<EstadoCelda>() is { Count: > 0 } estados)
            celdas = celdas.Where(c => estados.Contains(c.Estado));
        var vehiculos = f.Vehiculos;
        if (filtro.Placa is { } placa)
            vehiculos = vehiculos.Where(v => v.IdentificadorNorm.StartsWith(placa));

        return from c in celdas
               join v in vehiculos on new { c.TenantId, Id = c.VehiculoId } equals new { v.TenantId, v.Id }
               join k in cabeceras on new { c.TenantId, Id = c.DiagnosticoId } equals new { k.TenantId, k.Id }
               select new FilaLeida
               {
                   Id = c.Id,
                   Empresa = c.TenantId,
                   CreadoEn = k.CreadoEn,
                   DiagnosticoNombre = k.Nombre,
                   Placa = v.Placa,
                   Fuente = c.Fuente,
                   Estado = c.Estado,
                   MotivoNa = c.MotivoNa,
                   TipoError = c.TipoError,
                   DatoObtenidoEn = c.DatoObtenidoEn,
                   Reutilizada = c.ReutilizadaDeId != null,
                   Origen = c.Origen,
               };
    }

    private sealed class FilaLeida
    {
        public Guid Id { get; init; }
        public Guid Empresa { get; init; }
        public DateTimeOffset CreadoEn { get; init; }
        public string DiagnosticoNombre { get; init; } = string.Empty;
        public string? Placa { get; init; }
        public Fuente Fuente { get; init; }
        public EstadoCelda Estado { get; init; }
        public MotivoNa? MotivoNa { get; init; }
        public string? TipoError { get; init; }
        public DateTimeOffset? DatoObtenidoEn { get; init; }
        public bool Reutilizada { get; init; }
        public OrigenCelda? Origen { get; init; }
    }
}

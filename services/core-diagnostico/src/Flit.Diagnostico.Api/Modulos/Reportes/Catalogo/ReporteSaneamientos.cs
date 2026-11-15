using Flit.Diagnostico.Domain.Saneamiento;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Reporte <c>saneamientos</c> (HU #14579, BE-2; contrato-api §4): un renglón por saneamiento solicitado en el rango de un lote visible,
/// con los renglones vigentes (sin quitar) y los días hasta el cierre. Sin los usuarios que lo solicitaron, tomaron o cerraron (SP-16).
/// </summary>
internal sealed class ReporteSaneamientos : IReporte
{
    public TipoReporte Tipo => TipoReporte.Saneamientos;

    public Task<int> ContarAsync(ContextoReporte contexto, FiltroReporte filtro, CancellationToken ct) => Consulta(contexto, filtro).CountAsync(ct);

    public async Task<IReadOnlyList<FilaReporte>> LeerAsync(ContextoReporte contexto, FiltroReporte filtro, int saltar, int? tomar, CancellationToken ct)
    {
        var consulta = Consulta(contexto, filtro).OrderByDescending(f => f.SolicitadoEn).ThenBy(f => f.Id).Skip(saltar);
        if (tomar is { } t)
            consulta = consulta.Take(t);
        var leidas = await consulta.ToListAsync(ct).ConfigureAwait(false);
        return [.. leidas.Select(l =>
        {
            var fila = new FilaReporte { Empresa = l.Empresa };
            var v = fila.Valores;
            v["solicitado_en"] = ValoresReporte.Instante(l.SolicitadoEn);
            v["placa"] = l.Placa;
            v["diagnostico_nombre"] = l.DiagnosticoNombre;
            v["estado"] = ValoresReporte.Codigo<EstadoSaneamiento>(l.Estado);
            v["tomado_en"] = ValoresReporte.Instante(l.TomadoEn);
            v["cerrado_en"] = ValoresReporte.Instante(l.CerradoEn);
            v["dias_hasta_cierre"] = l.CerradoEn is { } cerrado ? (int)Math.Floor((cerrado - l.SolicitadoEn).TotalDays) : null;
            v["motivo_cierre"] = l.MotivoCierre;
            v["renglones_total"] = l.RenglonesTotal;
            v["renglones_pendientes"] = l.RenglonesPendientes;
            return fila;
        })];
    }

    private static IQueryable<FilaLeida> Consulta(ContextoReporte contexto, FiltroReporte filtro)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(filtro);
        var f = contexto.Fuentes;
        var (desde, hasta) = (filtro.DesdeInstante, filtro.HastaInstante);
        var saneamientos = f.Saneamientos.Where(s => s.SolicitadoEn >= desde && s.SolicitadoEn < hasta);
        if (filtro.Estados<EstadoSaneamiento>() is { Count: > 0 } estados)
            saneamientos = saneamientos.Where(s => estados.Contains(s.Estado));
        var vehiculos = f.Vehiculos;
        if (filtro.Placa is { } placa)
            vehiculos = vehiculos.Where(v => v.IdentificadorNorm.StartsWith(placa));
        var renglones = f.Renglones.Where(r => r.QuitadoEn == null);

        return from s in saneamientos
               join v in vehiculos on new { s.TenantId, Id = s.VehiculoId } equals new { v.TenantId, v.Id }
               join k in f.Cabeceras on new { s.TenantId, Id = s.DiagnosticoId } equals new { k.TenantId, k.Id }
               select new FilaLeida
               {
                   Id = s.Id,
                   Empresa = s.TenantId,
                   SolicitadoEn = s.SolicitadoEn,
                   Placa = v.Placa,
                   DiagnosticoNombre = k.Nombre,
                   Estado = s.Estado,
                   TomadoEn = s.TomadoEn,
                   CerradoEn = s.CerradoEn,
                   MotivoCierre = s.MotivoCierre,
                   RenglonesTotal = renglones.Count(r => r.TenantId == s.TenantId && r.SaneamientoId == s.Id),
                   RenglonesPendientes = renglones.Count(r => r.TenantId == s.TenantId && r.SaneamientoId == s.Id && r.Resultado == ResultadoRenglonSaneamiento.Pendiente),
               };
    }

    private sealed class FilaLeida
    {
        public Guid Id { get; init; }
        public Guid Empresa { get; init; }
        public DateTimeOffset SolicitadoEn { get; init; }
        public string? Placa { get; init; }
        public string DiagnosticoNombre { get; init; } = string.Empty;
        public EstadoSaneamiento Estado { get; init; }
        public DateTimeOffset? TomadoEn { get; init; }
        public DateTimeOffset? CerradoEn { get; init; }
        public string? MotivoCierre { get; init; }
        public int RenglonesTotal { get; init; }
        public int RenglonesPendientes { get; init; }
    }
}

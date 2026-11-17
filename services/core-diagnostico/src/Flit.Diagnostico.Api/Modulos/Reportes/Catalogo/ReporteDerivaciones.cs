using Flit.Diagnostico.Domain.Derivacion;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Reporte <c>derivaciones</c> (HU #14579, BE-2; contrato-api §4): un renglón por ítem de las operaciones «Crear trámite» creadas en el
/// rango sobre lotes visibles, con el tipo de trámite (nombre del catálogo), el estado, el motivo y el número de trámite. Sin el usuario
/// que la pidió ni el que la reintentó (SP-16).
/// </summary>
internal sealed class ReporteDerivaciones(NombresTramite tramites) : IReporte
{
    public TipoReporte Tipo => TipoReporte.Derivaciones;

    public Task<int> ContarAsync(ContextoReporte contexto, FiltroReporte filtro, CancellationToken ct) => Consulta(contexto, filtro).CountAsync(ct);

    public async Task<IReadOnlyList<FilaReporte>> LeerAsync(ContextoReporte contexto, FiltroReporte filtro, int saltar, int? tomar, CancellationToken ct)
    {
        var consulta = Consulta(contexto, filtro).OrderByDescending(f => f.CreadaEn).ThenBy(f => f.Id).Skip(saltar);
        if (tomar is { } t)
            consulta = consulta.Take(t);
        var leidas = await consulta.ToListAsync(ct).ConfigureAwait(false);
        var nombres = await tramites.NombresAsync(leidas.Select(l => (string?)l.TipoTramiteCodigo), ct).ConfigureAwait(false);
        return [.. leidas.Select(l =>
        {
            var fila = new FilaReporte { Empresa = l.Empresa };
            var v = fila.Valores;
            v["fecha"] = ValoresReporte.Instante(l.CreadaEn);
            v["placa"] = l.Placa;
            v["diagnostico_nombre"] = l.DiagnosticoNombre;
            v["tipo_operacion"] = ValoresReporte.Codigo<TipoOperacionDerivacion>(l.TipoOperacion);
            v["tipo_tramite"] = nombres.GetValueOrDefault(l.TipoTramiteCodigo, l.TipoTramiteCodigo);
            v["estado"] = ValoresReporte.Codigo<EstadoItemDerivacion>(l.Estado);
            v["motivo"] = l.Motivo;
            v["tramite_numero"] = l.TramiteNumero;
            return fila;
        })];
    }

    private static IQueryable<FilaLeida> Consulta(ContextoReporte contexto, FiltroReporte filtro)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(filtro);
        var f = contexto.Fuentes;
        var (desde, hasta) = (filtro.DesdeInstante, filtro.HastaInstante);
        var operaciones = f.Operaciones.Where(o => o.CreadaEn >= desde && o.CreadaEn < hasta);
        var items = f.Derivaciones;
        if (filtro.Estados<EstadoItemDerivacion>() is { Count: > 0 } estados)
            items = items.Where(i => estados.Contains(i.Estado));
        var vehiculos = f.Vehiculos;
        if (filtro.Placa is { } placa)
            vehiculos = vehiculos.Where(v => v.IdentificadorNorm.StartsWith(placa));

        return from i in items
               join o in operaciones on new { i.TenantId, Id = i.OperacionId } equals new { o.TenantId, o.Id }
               join v in vehiculos on new { i.TenantId, Id = i.VehiculoId } equals new { v.TenantId, v.Id }
               join k in f.Cabeceras on new { o.TenantId, Id = o.DiagnosticoId } equals new { k.TenantId, k.Id }
               select new FilaLeida
               {
                   Id = i.Id,
                   Empresa = i.TenantId,
                   CreadaEn = o.CreadaEn,
                   Placa = v.Placa,
                   DiagnosticoNombre = k.Nombre,
                   TipoOperacion = o.Tipo,
                   TipoTramiteCodigo = o.TipoTramiteCodigo,
                   Estado = i.Estado,
                   Motivo = i.MotivoCodigo,
                   TramiteNumero = i.TramiteNumero,
               };
    }

    private sealed class FilaLeida
    {
        public Guid Id { get; init; }
        public Guid Empresa { get; init; }
        public DateTimeOffset CreadaEn { get; init; }
        public string? Placa { get; init; }
        public string DiagnosticoNombre { get; init; } = string.Empty;
        public TipoOperacionDerivacion TipoOperacion { get; init; }
        public string TipoTramiteCodigo { get; init; } = string.Empty;
        public EstadoItemDerivacion Estado { get; init; }
        public string? Motivo { get; init; }
        public string? TramiteNumero { get; init; }
    }
}

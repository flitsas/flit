using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// Elegibilidad de un vehículo ya evaluada, con el estado de su último ítem y su trámite. <c>Veredicto</c> (HU #14563) es el del
/// dictamen vigente; null si no hay o si el modo no exige dictamen (Simple).
/// </summary>
internal sealed record ElegibilidadVehiculo(Guid VehiculoId, string? Motivo, EstadoItemDerivacion? UltimoEstado, Guid? TramiteId, string? TramiteNumero, string? Veredicto = null)
{
    public bool Elegible => Motivo is null;
}

/// <summary>
/// Lee de la compañía del contexto (filtro global por empresa) lo que necesita <see cref="ElegibilidadDerivacion"/> para un conjunto
/// de vehículos de un lote, en cuatro consultas sin N+1 (Punto 4, diseño §5.2): trámite del vehículo, estados de sus celdas
/// (terminado, R-7), veredicto del dictamen vigente (solo si el lote es Con análisis) y sus ítems (vivo y último estado). La usan la
/// grilla, el individual y el masivo (incremento 3) al encolar, al reintentar fallidas y al ejecutar cada ítem; el consumidor pasa
/// su propio ítem en <c>excluirItem</c> (si no, su ítem ENVIADO lo haría EN_CURSO). Vehículo que no es del lote: no aparece en el
/// resultado.
/// </summary>
internal static class LecturaElegibilidad
{
    public static async Task<IReadOnlyDictionary<Guid, ElegibilidadVehiculo>> LeerAsync(
        DiagnosticoDb db, Guid diagnosticoId, ModoDiagnostico modo, IReadOnlyCollection<Guid> vehiculos, CancellationToken ct, Guid? excluirItem = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(vehiculos);
        if (vehiculos.Count == 0)
            return new Dictionary<Guid, ElegibilidadVehiculo>();
        var ids = vehiculos.Distinct().ToList();
        var filas = await db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.DiagnosticoId == diagnosticoId && ids.Contains(v.Id))
            .Select(v => new { v.Id, v.TramiteId, v.TramiteNumero })
            .ToListAsync(ct).ConfigureAwait(false);
        var estados = (await db.Set<CeldaConsulta>().AsNoTracking()
                .Where(c => c.DiagnosticoId == diagnosticoId && ids.Contains(c.VehiculoId))
                .Select(c => new { c.VehiculoId, c.Estado })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(c => c.VehiculoId, c => c.Estado);
        var exige = ElegibilidadDerivacion.ExigeDictamen(modo);
        var veredictos = exige
            ? (await db.Set<DictamenVehiculo>().AsNoTracking()
                    .Where(d => d.Vigente && ids.Contains(d.VehiculoId))
                    .Select(d => new { d.VehiculoId, d.Veredicto })
                    .ToListAsync(ct).ConfigureAwait(false))
                .ToDictionary(d => d.VehiculoId, d => d.Veredicto)
            : [];
        var items = (await db.Set<ItemDerivacion>().AsNoTracking()
                .Where(i => ids.Contains(i.VehiculoId) && (excluirItem == null || i.Id != excluirItem))
                .Select(i => new { i.VehiculoId, i.Estado, i.ActualizadoEn, i.Id })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(i => i.VehiculoId);

        return filas.ToDictionary(v => v.Id, v =>
        {
            var propios = items[v.Id].ToList();
            var datos = new DatosElegibilidad(
                Terminado: Domain.Diagnosticos.AvanceVehiculo.Terminado(estados[v.Id]),
                ExigeDictamen: exige,
                Veredicto: veredictos.GetValueOrDefault(v.Id),
                TieneTramite: v.TramiteId is not null,
                ItemVivo: propios.Any(i => i.Estado is EstadoItemDerivacion.Pendiente or EstadoItemDerivacion.Enviado));
            var ultimo = propios.OrderByDescending(i => i.ActualizadoEn).ThenByDescending(i => i.Id).FirstOrDefault();
            return new ElegibilidadVehiculo(v.Id, ElegibilidadDerivacion.MotivoNoElegible(datos), ultimo?.Estado, v.TramiteId, v.TramiteNumero, datos.Veredicto);
        });
    }

    /// <summary>El DTO de la fila de la grilla.</summary>
    public static DerivacionFilaDto Fila(ElegibilidadVehiculo e, OpcionesDerivacion opciones, Guid empresa)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(opciones);
        return new DerivacionFilaDto(
            e.Elegible,
            e.Motivo,
            e.UltimoEstado is { } estado ? CodigosDeEnum.Codigo(estado) : null,
            e.TramiteId is { } id ? new TramiteFilaDto(id, e.TramiteNumero ?? string.Empty, opciones.UrlTramite(id, empresa)) : null);
    }
}

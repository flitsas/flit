using Flit.Comparendos.Api.Modulos.Bandeja;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Reportes;

/// <summary>Organismo de tránsito por cantidad de comparendos pendientes (las «secretarías con más comparendos» de V1).</summary>
public sealed record OrganismoFrecuente(Guid? OrganismoId, string Nombre, int Cantidad, decimal Valor);

/// <summary>
/// Pestaña Gestión de Reportes 2.0: la lente de operación que salió del Inicio (decisión 1 del contrato) con el periodo y
/// el NIT de los reportes, más el top de organismos. Sin periodo es exactamente <c>/comparendos/operacion</c>.
/// </summary>
public sealed record Gestion(bool ConPeriodo, Operacion Operacion, IReadOnlyList<OrganismoFrecuente> TopOrganismos);

public sealed partial class ServicioReportes
{
    public async Task<Gestion> GestionAsync(Alcance a, ConsultaBandeja bandeja, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        // Gestión solo existe con el módulo de reportes y ReportesVer: siempre con pesos.
        var operacion = await OperacionAsync(a, conValores: true, bandeja, ct).ConfigureAwait(false);

        var top = await Pendientes(a).GroupBy(c => c.OrganismoTransitoId)
            .Select(g => new { Organismo = g.Key, N = g.Count(), V = g.Sum(c => c.ValorTotal ?? 0) })
            .OrderByDescending(x => x.N).ThenBy(x => x.Organismo).Take(11).ToListAsync(ct).ConfigureAwait(false);
        var ids = top.Where(o => o.Organismo != null).Select(o => o.Organismo!.Value).ToList();
        var nombres = await db.OrganismosTransito.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Nombre, ct).ConfigureAwait(false);
        var organismos = top
            .Select(o => new OrganismoFrecuente(o.Organismo, o.Organismo is { } id && nombres.TryGetValue(id, out var n) ? n : "Sin organismo identificado", o.N, o.V))
            .OrderByDescending(o => o.Cantidad).ThenBy(o => o.Nombre, StringComparer.Ordinal).Take(10).ToList();

        return new Gestion(ConPeriodo(a), operacion, organismos);
    }
}

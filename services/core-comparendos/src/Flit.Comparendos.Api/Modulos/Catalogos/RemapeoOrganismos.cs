using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Catalogos;

/// <summary>
/// GDC punto 3: tras sincronizar el catálogo o crear un alias, los comparendos que siguen sin organismo se vuelven a
/// mapear con las mismas reglas del ingreso (<see cref="MapeadorOt"/>: alias o nombre, código de 8 cifras y municipio),
/// es decir con el texto de la secretaría y el número. Cada asignación deja su evento en la línea de tiempo (RN-009).
/// </summary>
public sealed class RemapeoOrganismos(ComparendosDb db, Timeline timeline, Reloj reloj)
{
    private const int Lote = 500;

    /// <returns>Cuántos comparendos quedaron con organismo.</returns>
    public async Task<int> RemapearSinOrganismoAsync(Actor actor, string motivo, object? datos, CancellationToken ct)
    {
        // Un mapeador nuevo: el del ámbito puede tener en caché el catálogo de antes del cambio.
        var mapeador = new MapeadorOt(db, reloj);
        var pendientes = await db.Comparendos.AsNoTracking().Where(c => c.OrganismoTransitoId == null)
            .Select(c => new { c.Id, c.SecretariaTexto, c.NumeroNormalizado }).ToListAsync(ct).ConfigureAwait(false);
        var asignados = new Dictionary<Guid, Guid>();
        foreach (var p in pendientes)
        {
            if (await mapeador.MapearAsync(p.SecretariaTexto, ct, p.NumeroNormalizado).ConfigureAwait(false) is { } ot)
                asignados[p.Id] = ot;
        }

        if (asignados.Count == 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false); // los alias AUTOMATICO que haya dejado el mapeador
            return 0;
        }

        var nombres = await db.OrganismosTransito.AsNoTracking().Where(o => asignados.Values.Distinct().Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Nombre, ct).ConfigureAwait(false);
        foreach (var ids in asignados.Keys.Chunk(Lote))
        {
            foreach (var c in await db.Comparendos.Where(c => ids.Contains(c.Id) && c.OrganismoTransitoId == null).ToListAsync(ct).ConfigureAwait(false))
            {
                var ot = asignados[c.Id];
                c.OrganismoTransitoId = ot;
                timeline.Agregar(c, TipoEventoTimeline.DatosActualizados, $"Organismo de tránsito asignado: {nombres[ot]} ({motivo}).", actor, new { organismo = ot, detalle = datos });
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return asignados.Count;
    }
}

using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Textos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Catalogos;

/// <summary>
/// Traduce la secretaría que trae una fuente al organismo de tránsito del catálogo (MOD02 RN-15): primero los alias;
/// si no hay, coincidencia exacta con el nombre o el código del OT, que queda guardada como alias AUTOMATICO para la
/// próxima vez. Sin coincidencia, el comparendo queda sin OT (RN-044) hasta que el SuperAdmin cree el alias.
/// </summary>
public sealed class MapeadorOt(ComparendosDb db, Reloj reloj)
{
    private readonly Dictionary<string, Guid?> _cache = new(StringComparer.Ordinal);

    public async Task<Guid?> MapearAsync(string? secretaria, CancellationToken ct)
    {
        var texto = TextoNormalizado.De(secretaria);
        if (texto.Length == 0)
            return null;
        if (_cache.TryGetValue(texto, out var conocido))
            return conocido;

        var alias = await db.OrganismosTransitoAlias.AsNoTracking().Where(a => a.TextoNormalizado == texto).Select(a => (Guid?)a.OrganismoTransitoId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (alias is null)
        {
            var candidatos = await db.OrganismosTransito.AsNoTracking().Select(o => new { o.Id, o.Codigo, o.Nombre }).ToListAsync(ct).ConfigureAwait(false);
            var exacto = candidatos.FirstOrDefault(o => o.Codigo == texto || TextoNormalizado.De(o.Nombre) == texto);
            if (exacto is not null)
            {
                alias = exacto.Id;
                if (texto.Length <= 250 && !db.OrganismosTransitoAlias.Local.Any(a => a.TextoNormalizado == texto))
                {
                    db.OrganismosTransitoAlias.Add(new OrganismoTransitoAlias
                    {
                        Id = Guid.CreateVersion7(),
                        TextoNormalizado = texto,
                        OrganismoTransitoId = exacto.Id,
                        Origen = "AUTOMATICO",
                        CreadoEn = reloj.Ahora,
                    });
                }
            }
        }

        _cache[texto] = alias;
        return alias;
    }
}

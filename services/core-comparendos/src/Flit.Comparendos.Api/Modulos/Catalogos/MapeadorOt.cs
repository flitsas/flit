using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Textos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Catalogos;

/// <summary>
/// Traduce la secretaría que trae una fuente al organismo de tránsito del catálogo (MOD02 RN-15): primero los alias;
/// si no hay, coincidencia exacta con el nombre o el código del OT, que queda guardada como alias AUTOMATICO para la
/// próxima vez. Si el texto no coincide, el número nacional del comparendo dice el OT: sus primeras 8 cifras son el
/// código del organismo (V1, BackCrudInfraction trafficSecretaryRepository.findByInfractionNumber); aquí solo con
/// coincidencia exacta, sin el recorte a 5 cifras de V1, y sin crear alias (el alias es del texto). Sin coincidencia,
/// el comparendo queda sin OT (RN-044) hasta que el SuperAdmin cree el alias.
/// </summary>
public sealed class MapeadorOt(ComparendosDb db, Reloj reloj)
{
    private readonly Dictionary<string, Guid?> _cache = new(StringComparer.Ordinal);
    private Dictionary<string, Guid>? _porCodigo;

    public async Task<Guid?> MapearAsync(string? secretaria, CancellationToken ct, string? numeroComparendo = null) =>
        await PorTextoAsync(secretaria, ct).ConfigureAwait(false) ?? await PorNumeroAsync(numeroComparendo, ct).ConfigureAwait(false);

    /// <summary>
    /// El OT según el número nacional del comparendo (≥ 16 dígitos: código del organismo de 8 cifras + consecutivo).
    /// Los códigos de 7 cifras del catálogo perdieron el cero inicial (Medellín 5001000 → 05001000).
    /// </summary>
    public async Task<Guid?> PorNumeroAsync(string? numero, CancellationToken ct)
    {
        if (numero is null || numero.Length < 16 || !numero.All(char.IsAsciiDigit))
            return null;
        _porCodigo ??= (await db.OrganismosTransito.AsNoTracking().Select(o => new { o.Id, o.Codigo }).ToListAsync(ct).ConfigureAwait(false))
            .Where(o => o.Codigo.Length is > 0 and <= 8 && o.Codigo.All(char.IsAsciiDigit))
            .GroupBy(o => o.Codigo.PadLeft(8, '0'))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Id, StringComparer.Ordinal);
        return _porCodigo.TryGetValue(numero[..8], out var id) ? id : null;
    }

    private async Task<Guid?> PorTextoAsync(string? secretaria, CancellationToken ct)
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

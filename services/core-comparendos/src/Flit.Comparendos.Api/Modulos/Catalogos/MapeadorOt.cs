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
/// coincidencia exacta y sin crear alias (el alias es del texto). Si el código exacto no está, se usa el municipio:
/// las 5 primeras cifras del número (DIVIPOLA) o el texto igual al nombre del municipio, solo cuando ese municipio
/// tiene un único OT en el catálogo. Lo pide la respuesta real del SIMIT: las sedes departamentales llegan con su
/// código de oficina (70670001 frente a 70670000 del catálogo) y algunas secretarías solo con el municipio («Galapa»).
/// Sin coincidencia, el comparendo queda sin OT (RN-044) hasta que el SuperAdmin cree el alias.
/// Los organismos gemelos (<see cref="OrganismosGemelos"/>: solo difieren en espacios) cuentan como uno, su titular.
/// </summary>
public sealed class MapeadorOt(ComparendosDb db, Reloj reloj)
{
    private readonly Dictionary<string, Guid?> _cache = new(StringComparer.Ordinal);
    private Dictionary<string, Guid>? _porCodigo;
    private Dictionary<string, Guid>? _porDivipola;
    private Dictionary<string, Guid>? _porMunicipio;
    private Dictionary<Guid, Guid>? _titulares;

    /// <summary>Organismos con el id de su titular: un gemelo cuenta como su titular.</summary>
    private async Task<List<(Guid Id, string Codigo, string Municipio)>> OrganismosAsync(CancellationToken ct)
    {
        var todos = await db.OrganismosTransito.AsNoTracking().Select(o => new { o.Id, o.Codigo, o.Nombre, o.Municipio }).ToListAsync(ct).ConfigureAwait(false);
        _titulares ??= OrganismosGemelos.Titulares(todos, o => o.Id, o => o.Codigo, o => o.Nombre);
        return [.. todos.Select(o => (_titulares[o.Id], o.Codigo, o.Municipio ?? string.Empty))];
    }

    private Guid Titular(Guid id) => _titulares is not null && _titulares.TryGetValue(id, out var t) ? t : id;

    public async Task<Guid?> MapearAsync(string? secretaria, CancellationToken ct, string? numeroComparendo = null) =>
        await PorTextoAsync(secretaria, ct).ConfigureAwait(false)
        ?? await PorNumeroAsync(numeroComparendo, ct).ConfigureAwait(false)
        ?? await PorMunicipioAsync(secretaria, numeroComparendo, ct).ConfigureAwait(false);

    /// <summary>
    /// El OT según el número nacional del comparendo (≥ 16 dígitos: código del organismo de 8 cifras + consecutivo).
    /// Los códigos de 7 cifras del catálogo perdieron el cero inicial (Medellín 5001000 → 05001000).
    /// </summary>
    public async Task<Guid?> PorNumeroAsync(string? numero, CancellationToken ct)
    {
        if (numero is null || numero.Length < 16 || !numero.All(char.IsAsciiDigit))
            return null;
        _porCodigo ??= (await OrganismosAsync(ct).ConfigureAwait(false))
            .Where(o => OrganismosGemelos.Codigo8(o.Codigo) is not null)
            .GroupBy(o => OrganismosGemelos.Codigo8(o.Codigo)!)
            .Where(g => g.Select(o => o.Id).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        return _porCodigo.TryGetValue(numero[..8], out var id) ? id : null;
    }

    /// <summary>El único OT del municipio, por las 5 primeras cifras del número nacional o por el texto de la secretaría.</summary>
    public async Task<Guid?> PorMunicipioAsync(string? secretaria, string? numero, CancellationToken ct)
    {
        if (_porDivipola is null || _porMunicipio is null)
        {
            var todos = await OrganismosAsync(ct).ConfigureAwait(false);
            _porDivipola = todos.Where(o => OrganismosGemelos.Codigo8(o.Codigo) is not null)
                .GroupBy(o => OrganismosGemelos.Codigo8(o.Codigo)![..5]).Where(g => g.Select(o => o.Id).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
            _porMunicipio = todos.Select(o => (o.Id, Texto: TextoNormalizado.De(o.Municipio))).Where(o => o.Texto.Length > 0)
                .GroupBy(o => o.Texto).Where(g => g.Select(o => o.Id).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        }

        if (numero is { Length: >= 16 } && numero.All(char.IsAsciiDigit) && _porDivipola.TryGetValue(numero[..5], out var porNumero))
            return porNumero;
        return _porMunicipio.TryGetValue(TextoNormalizado.De(secretaria), out var porTexto) ? porTexto : null;
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
            _titulares ??= OrganismosGemelos.Titulares(candidatos, o => o.Id, o => o.Codigo, o => o.Nombre);
            var exacto = candidatos.FirstOrDefault(o => o.Codigo == texto || TextoNormalizado.De(o.Nombre) == texto);
            if (exacto is not null)
            {
                alias = Titular(exacto.Id);
                if (texto.Length <= 250 && !db.OrganismosTransitoAlias.Local.Any(a => a.TextoNormalizado == texto))
                {
                    db.OrganismosTransitoAlias.Add(new OrganismoTransitoAlias
                    {
                        Id = Guid.CreateVersion7(),
                        TextoNormalizado = texto,
                        OrganismoTransitoId = alias.Value,
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

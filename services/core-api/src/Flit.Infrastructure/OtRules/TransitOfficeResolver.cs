using System.Text;
using System.Text.RegularExpressions;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Tramites.Domain.Integration;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="ITransitOfficeResolver"/> (B11, HU #10659): resuelve el OT
/// habilitado de la empresa por su nombre RUNT usando la MISMA lógica que
/// <c>GET /api/v1/tramites/transit-offices</c> — los grants vigentes del tenant
/// (<see cref="ITransitGrantRepository"/>) resueltos contra el catálogo
/// (<see cref="ITransitOfficeCatalog"/>).
///
/// El RUNT/Verifik solo devuelve el NOMBRE del organismo (no el código DIVIPOLA), así que el amarre
/// del OT destino en traspaso es por nombre. El match es TOLERANTE: normaliza ambos lados —minúsculas,
/// tildes, y espacios colapsados— para que "SABANETA", "Sabaneta" o "  Sabaneta " casen con el nombre
/// catalogado. Sin normalizar, una diferencia mínima dejaba <c>transit_office_id</c> sin fijar y las
/// políticas por-OT (RNMC, restricciones, bloqueo) no aplicaban aunque el OT fuera el correcto.
/// </summary>
internal sealed class TransitOfficeResolver : ITransitOfficeResolver
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly ITransitGrantRepository _grants;
    private readonly ITransitOfficeCatalog _catalog;

    public TransitOfficeResolver(ITransitGrantRepository grants, ITransitOfficeCatalog catalog)
    {
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(
        Guid tenantId,
        string transitOfficeName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transitOfficeName))
        {
            return null;
        }

        var enabledIds = await _grants
            .ListEnabledOfficeIdsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        var target = NormalizeName(transitOfficeName);
        var match = enabledIds
            .Select(_catalog.GetById)
            .FirstOrDefault(o => o is not null && NormalizeName(o.Name) == target);

        return match is null
            ? null
            : new ResolvedTransitOffice(match.Id, match.Code, match.Name, match.CityCode);
    }

    /// <summary>
    /// Normaliza un nombre de OT para comparar de forma tolerante: recorta, colapsa espacios internos,
    /// pliega diacríticos españoles (á→a, ñ→n, …) y pasa a mayúsculas invariantes. Así el nombre que
    /// trae el RUNT/Verifik (solo texto, sin DIVIPOLA) casa con el catálogo pese a diferencias de
    /// tildes, mayúsculas o espacios.
    ///
    /// El plegado es MANUAL por code point y NO usa <c>String.Normalize</c>/ICU a propósito: la app
    /// corre con <c>InvariantGlobalization=true</c> (Directory.Build.props), donde
    /// <c>Normalize(FormD)</c> no descompone acentos y cualquier "insensible a tildes" vía ICU queda
    /// mudo. La decodificación UTF-8 del HTTP/BD sí funciona (es codec, no cultura), así que en runtime
    /// el nombre llega con la vocal acentuada real que este plegado convierte.
    /// </summary>
    private static string NormalizeName(string value)
    {
        var collapsed = WhitespaceRegex.Replace(value.Trim(), " ");

        var sb = new StringBuilder(collapsed.Length);
        foreach (var ch in collapsed)
            sb.Append(FoldDiacritic(ch));

        return sb.ToString().ToUpperInvariant();
    }

    /// <summary>
    /// Pliega los diacríticos del español a su vocal/consonante base (mayús. y minús.). Devuelve el
    /// carácter tal cual si no es acentuado. Cubre á/é/í/ó/ú, diéresis (ü), variantes (à/â/ä/ã…) y ñ.
    /// </summary>
    private static char FoldDiacritic(char c) => (int)c switch
    {
        0x00E1 or 0x00E0 or 0x00E4 or 0x00E2 or 0x00E3 => 'a',
        0x00C1 or 0x00C0 or 0x00C4 or 0x00C2 or 0x00C3 => 'A',
        0x00E9 or 0x00E8 or 0x00EB or 0x00EA => 'e',
        0x00C9 or 0x00C8 or 0x00CB or 0x00CA => 'E',
        0x00ED or 0x00EC or 0x00EF or 0x00EE => 'i',
        0x00CD or 0x00CC or 0x00CF or 0x00CE => 'I',
        0x00F3 or 0x00F2 or 0x00F6 or 0x00F4 or 0x00F5 => 'o',
        0x00D3 or 0x00D2 or 0x00D6 or 0x00D4 or 0x00D5 => 'O',
        0x00FA or 0x00F9 or 0x00FC or 0x00FB => 'u',
        0x00DA or 0x00D9 or 0x00DC or 0x00DB => 'U',
        0x00F1 => 'n',
        0x00D1 => 'N',
        _ => c,
    };
}

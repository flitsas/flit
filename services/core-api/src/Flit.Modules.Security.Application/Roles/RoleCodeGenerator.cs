using System.Globalization;
using System.Text;
using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

/// <summary>
/// HU #13653 / #13666 (Épica #12753) — el código de un rol nuevo no lo escribe la persona: se genera a partir del nombre.
/// Es un identificador interno que nadie ve en pantalla (misma línea que el COMP-XXXX de la Épica #13517).
/// <para>
/// Regla: minúsculas, sin tildes ni ñ (á→a, ñ→n), cualquier otro carácter → <c>_</c> (sin repetir ni en los bordes),
/// solo <c>[a-z0-9_]</c>, máximo <see cref="MaxLength"/> (la columna <c>security.roles.code</c> es varchar(50) y el
/// patrón vigente admite 2 a 50). Si el nombre no deja al menos <see cref="MinLength"/> caracteres, se usa
/// <see cref="Fallback"/>. Si el código ya está tomado se agrega <c>_2</c>, <c>_3</c>… recortando la base para no pasar
/// del máximo. Ejemplo: «Coordinación Señal» → <c>coordinacion_senal</c>; el segundo → <c>coordinacion_senal_2</c>.
/// </para>
/// </summary>
public static class RoleCodeGenerator
{
    public const int MaxLength = 50;
    public const int MinLength = 2;
    public const string Fallback = "rol";

    /// <summary>Tope de sufijos que se prueban antes de rendirse (en la práctica nunca se acerca).</summary>
    public const int MaxSuffix = 10_000;

    /// <summary>
    /// Largo del prefijo con que se buscan los códigos tomados: cualquier candidato (base o base_N con N ≤
    /// <see cref="MaxSuffix"/>) empieza por él, porque el sufijo más largo («_10000») recorta la base a 44.
    /// </summary>
    public const int SearchPrefixLength = 44;

    /// <summary>Código base a partir del nombre (sin sufijo).</summary>
    public static string FromName(string? name)
    {
        var decomposed = (name ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var raw in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark)
                continue; // la tilde de á/ñ se descompone en una marca aparte: se descarta y queda a/n.

            var c = Fold(char.ToLowerInvariant(raw));
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');
        }

        var code = Clip(sb.ToString(), MaxLength);
        return code.Length < MinLength ? Fallback : code;
    }

    /// <summary>El candidato número <paramref name="n"/>: la base tal cual para 1, <c>base_n</c> desde 2.</summary>
    public static string Candidate(string baseCode, int n)
    {
        if (n <= 1)
            return baseCode;
        var suffix = "_" + n.ToString(CultureInfo.InvariantCulture);
        return Clip(baseCode, MaxLength - suffix.Length) + suffix;
    }

    /// <summary>Prefijo para buscar en BD los códigos que podrían chocar con los candidatos de esta base.</summary>
    public static string SearchPrefix(string baseCode) => Clip(baseCode, SearchPrefixLength);

    /// <summary>
    /// Primer candidato libre. <paramref name="taken"/> se compara sin mayúsculas (los índices de tenant y el disparador
    /// de codes reservados comparan con <c>lower(code)</c>).
    /// </summary>
    public static string FirstFree(string baseCode, IEnumerable<string> taken)
    {
        var set = taken.Select(t => t.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        for (var n = 1; n <= MaxSuffix; n++)
        {
            var candidate = Candidate(baseCode, n);
            if (!set.Contains(candidate))
                return candidate;
        }

        throw new RoleCodeDuplicateException();
    }

    /// <summary>
    /// Letras con tilde del español (y vecinas) a su base. Hace falta además de <c>Normalize(FormD)</c>: con
    /// <c>InvariantGlobalization</c> la normalización puede no descomponer y la á quedaría como «_».
    /// </summary>
    private static char Fold(char c) => c switch
    {
        'á' or 'à' or 'ä' or 'â' or 'ã' or 'å' => 'a',
        'é' or 'è' or 'ë' or 'ê' => 'e',
        'í' or 'ì' or 'ï' or 'î' => 'i',
        'ó' or 'ò' or 'ö' or 'ô' or 'õ' => 'o',
        'ú' or 'ù' or 'ü' or 'û' => 'u',
        'ñ' => 'n',
        'ç' => 'c',
        'ý' or 'ÿ' => 'y',
        _ => c,
    };

    private static string Clip(string value, int max)
    {
        var clipped = value.Length > max ? value[..max] : value;
        return clipped.Trim('_');
    }

    /// <summary>
    /// Crea el rol con un código generado. La unicidad la decide el servidor: se leen los códigos tomados en el ámbito
    /// real de la tabla, se elige el primero libre y se inserta; si otra petición se lo ganó en el medio, los índices
    /// únicos (<c>uq_roles_tenant_code</c>, <c>uq_roles_code_target_entity_type</c>) o el disparador de codes reservados
    /// responden 23505 (<see cref="RoleCodeDuplicateException"/>) y se reintenta con una lectura nueva.
    /// </summary>
    internal static async Task<Guid> CreateWithGeneratedCodeAsync(
        IRoleRepository repository,
        Guid? tenantId,
        string targetEntityType,
        string name,
        IEnumerable<string> reserved,
        Func<string, CreateRoleData> build,
        CancellationToken ct)
    {
        const int maxAttempts = 5;
        var baseCode = FromName(name);
        var reservedList = reserved.ToList();
        for (var attempt = 1; ; attempt++)
        {
            var taken = await repository.FindTakenCodesAsync(tenantId, targetEntityType, SearchPrefix(baseCode), ct);
            var code = FirstFree(baseCode, taken.Concat(reservedList));
            try
            {
                return await repository.CreateAsync(build(code), ct);
            }
            catch (RoleCodeDuplicateException) when (attempt < maxAttempts)
            {
                // Carrera con otra creación del mismo nombre: se vuelve a leer y se toma el siguiente libre.
            }
        }
    }
}

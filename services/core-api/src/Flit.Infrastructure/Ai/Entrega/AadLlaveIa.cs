using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Admin.Domain.Plataforma.Ai;

namespace Flit.Infrastructure.Ai.Entrega;

/// <summary>
/// AAD canónico del sobre de la llave (ADR-0091 §2, SP-11 con <c>modelo</c>): bytes UTF-8 separados por <c>\n</c>, sin espacios:
/// <c>flit.ia.llave.v1\n&lt;audiencia&gt;\n&lt;tool&gt;\n&lt;proveedor&gt;\n&lt;modelo&gt;\n&lt;row_version&gt;\n&lt;kid&gt;\n&lt;base64url(nonce)&gt;\n&lt;emitida_en epoch s&gt;</c>.
/// Cada campo se valida antes de componer (ninguno puede traer un salto de línea); números en decimal invariante; nonce en
/// base64url sin relleno. core-diagnostico lo recompone con su propia función y el vector compartido fija ambos lados.
/// </summary>
internal static partial class AadLlaveIa
{
    public const string Version = "flit.ia.llave.v1";

    public const int BytesNonce = 16;

    public static bool KidValido(string? kid) => kid is not null && KidRegex().IsMatch(kid);

    public static byte[] Componer(string audiencia, string toolCode, string proveedor, string modelo, long rowVersion, string kid, ReadOnlySpan<byte> nonce, long emitidaEnSegundos)
    {
        if (audiencia is null || !CodigoRegex().IsMatch(audiencia))
            throw new ArgumentException("Audiencia inválida.", nameof(audiencia));
        if (toolCode is null || !AiToolCodes.IsKnown(toolCode) || !string.Equals(AiToolCodes.Normalize(toolCode), toolCode, StringComparison.Ordinal))
            throw new ArgumentException("Herramienta fuera del catálogo.", nameof(toolCode));
        if (proveedor is null || !AiProviderCodes.IsKnown(proveedor))
            throw new ArgumentException("Proveedor fuera del catálogo.", nameof(proveedor));
        if (modelo is null || !ModeloRegex().IsMatch(modelo))
            throw new ArgumentException("Modelo inválido.", nameof(modelo));
        if (!KidValido(kid))
            throw new ArgumentException("kid inválido.", nameof(kid));
        if (nonce.Length != BytesNonce)
            throw new ArgumentException("El nonce debe tener 16 bytes.", nameof(nonce));

        var texto = string.Join('\n',
            Version,
            audiencia,
            toolCode,
            proveedor,
            modelo,
            rowVersion.ToString(CultureInfo.InvariantCulture),
            kid,
            Base64Url.EncodeToString(nonce),
            emitidaEnSegundos.ToString(CultureInfo.InvariantCulture));
        return Encoding.UTF8.GetBytes(texto);
    }

    // \z y no $: $ acepta un \n final.
    [GeneratedRegex(@"^[a-z0-9-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex KidRegex();

    [GeneratedRegex(@"^[a-z0-9_]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodigoRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._:-]{1,128}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ModeloRegex();
}

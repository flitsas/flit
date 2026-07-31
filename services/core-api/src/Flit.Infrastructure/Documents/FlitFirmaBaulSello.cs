using System.Globalization;
using System.Text;
using Flit.Tramites.Application.Documents;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Texto de trazabilidad de la firma del baúl (ADR-0025 §4): a quién pertenece la firma custodiada,
/// hasta cuándo está vigente y con qué código se registró.
///
/// <para><b>Por qué existe (HU #11170).</b> Este sello solo se pintaba en el FUR, dentro del mapper del
/// overlay. La compraventa, el contrato de mandato y la solicitud de trámite virtual estampaban la
/// imagen de la firma y nada más, así que quien firmaba por el baúl quedaba en esos tres documentos
/// sin vigencia ni hash: no había forma de verificar la firma leyendo el documento. Los metadatos ya
/// viajaban en <see cref="FurDocumentData.FirmaBaulMetadatos"/> hasta TODOS los generadores; lo único
/// que faltaba era pintarlos.</para>
///
/// <para>Y faltaba en el peor momento: hasta el Bug #11146, quien firmaba por el baúl arrastraba
/// ADEMÁS el sello de la validación de identidad —con su hash y sus fechas—, que tapaba la carencia.
/// Al hacer exclusivas las dos estampas, esos documentos se quedaron sin ningún dato verificable.</para>
///
/// <para><b>Dos formatos, una sola definición.</b> El FUR imprime la identificación del firmante porque
/// su espacio de firma no la lleva en ninguna otra parte; los demás documentos ya imprimen nombre y
/// documento bajo la línea de firma, así que repetirlos sería ruido y ocuparía un alto que el mandato
/// no tiene (HU #11034). De ahí <c>incluirIdentificacion</c>: cambia cuánto se imprime, nunca el
/// formato de los valores.</para>
/// </summary>
internal static class FlitFirmaBaulSello
{
    /// <summary>HU #11018 — formato de negocio único en documentos: AÑO/MES/DÍA, sin hora.</summary>
    private const string FormatoFecha = "yyyy/MM/dd";

    /// <summary>
    /// Arma el sello multilínea de una firma del baúl.
    /// </summary>
    /// <param name="meta">Metadatos de la firma custodiada resuelta para la parte.</param>
    /// <param name="incluirIdentificacion">
    /// <c>true</c> añade documento y nombre del firmante al principio (FUR); <c>false</c> deja solo
    /// vigencia y hash, para documentos que ya identifican al firmante bajo la línea.
    /// </param>
    internal static string Build(FirmaBaulMetadata meta, bool incluirIdentificacion)
    {
        ArgumentNullException.ThrowIfNull(meta);

        var lines = new List<string>();
        if (incluirIdentificacion)
        {
            lines.Add($"Doc. {meta.DocumentNumber}");
            lines.Add(meta.FullName);
        }

        lines.Add($"Vig. {meta.VigenciaDesde.ToString(FormatoFecha, CultureInfo.InvariantCulture)} — {meta.VigenciaHasta.ToString(FormatoFecha, CultureInfo.InvariantCulture)}");

        // HU #10930 (Feature #10929): se estampa el codigo_hash digitado en el baúl (meta.Hash), NO el
        // UUID de la fila. Si el baúl no trae código (firmas previas / null), se OMITE la línea "Hash"
        // en vez de imprimir el GUID (que confundía al operador).
        if (!string.IsNullOrWhiteSpace(meta.Hash))
            lines.Add($"Hash: {meta.Hash}");

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Sello de la firma del baúl del rol, o <c>null</c> si esa parte no firmó por el baúl.
    /// Tolera los alias de rol del FUR ("propietario"/"vendedor") igual que la resolución de la imagen:
    /// el sello y la imagen tienen que aparecer o faltar juntos.
    /// </summary>
    internal static string? Resolve(
        IReadOnlyDictionary<string, FirmaBaulMetadata>? metadatos,
        string? rol,
        bool incluirIdentificacion)
    {
        if (metadatos is null || string.IsNullOrWhiteSpace(rol))
            return null;

        foreach (var key in RolKeys(rol))
        {
            if (metadatos.TryGetValue(key, out var meta))
                return Build(meta, incluirIdentificacion);
        }

        return null;
    }

    /// <summary>
    /// Alias de rol para buscar en los diccionarios de firma. Definición ÚNICA: el mapper del FUR la
    /// usa para resolver la imagen y este helper para el sello, de modo que no puedan divergir y dejar
    /// una firma estampada sin su trazabilidad (o al revés).
    /// </summary>
    internal static IEnumerable<string> RolKeys(string rol)
    {
        var n = Norm(rol);
        yield return rol;
        if (n.Contains("COMPRADOR", StringComparison.Ordinal)) yield return "comprador";
        if (n.Contains("VENDEDOR", StringComparison.Ordinal)) yield return "vendedor";
        if (n.Contains("PROPIETARIO", StringComparison.Ordinal)) yield return "propietario";
    }

    /// <summary>Mayúsculas sin tildes, para comparar roles escritos de cualquier manera.</summary>
    private static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().ToUpperInvariant().Trim();
    }
}

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Correspondencia explícita entre el código del catálogo (<c>tramites.document_types.code</c>,
/// que es lo que el OT reordena) y el tipo del adjunto del trámite
/// (<c>ProcedureInstanceAttachment.Tipo</c>, que es lo que se fusiona en el PDF) — HU #11183.
/// <para>
/// En el checklist los dos coinciden porque el adjunto se guarda con el código del documento que
/// lo pide, pero en los generados no siempre: el certificado de vigencia SOAT/RTM está catalogado
/// como <c>certificado_vigencia_soat_rtm</c> y se adjunta como <c>certificado_soat_rtm</c>. Esa
/// diferencia rompía en silencio el orden configurado (el documento caía al final como si no
/// estuviera en la lista), así que la equivalencia se declara aquí, en un único sitio con test, en
/// vez de confiar en que las cadenas coincidan.
/// </para>
/// </summary>
public static class ConsolidadoDocumentCodeMap
{
    /// <summary>
    /// Códigos de catálogo cuyo adjunto se guarda con otro nombre. Un código puede cubrir varios
    /// tipos de adjunto; se expanden en el orden declarado.
    /// </summary>
    private static readonly Dictionary<string, string[]> Equivalencias =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["certificado_vigencia_soat_rtm"] = ["certificado_soat_rtm"],
        };

    /// <summary>
    /// Tipos de adjunto que representan a un documento del catálogo. Por defecto es el propio
    /// código (identidad): el catálogo se dio de alta con los mismos códigos que usan los adjuntos.
    /// </summary>
    public static IReadOnlyList<string> AttachmentTipos(string? catalogCode)
    {
        if (string.IsNullOrWhiteSpace(catalogCode))
        {
            return [];
        }

        var code = catalogCode.Trim();
        return Equivalencias.TryGetValue(code, out var tipos) ? tipos : [code];
    }

    /// <summary>
    /// Traduce el orden configurado por el OT (códigos del catálogo) a la prelación de tipos de
    /// adjunto que consume el ordenador del expediente. Deduplica conservando la primera posición:
    /// dos códigos que apunten al mismo adjunto no lo duplican en el PDF.
    /// </summary>
    public static IReadOnlyList<string> ToPrecedence(IEnumerable<string>? catalogCodes)
    {
        if (catalogCodes is null)
        {
            return [];
        }

        var precedence = new List<string>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in catalogCodes)
        {
            foreach (var tipo in AttachmentTipos(code))
            {
                if (vistos.Add(tipo))
                {
                    precedence.Add(tipo);
                }
            }
        }

        return precedence;
    }
}

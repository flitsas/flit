namespace Flit.Admin.Application.GeneracionDocumental.Prefill;

/// <summary>
/// Un campo prellenado del formulario, con la <b>fuente efectiva</b> del dato. La fuente viaja por
/// campo y no por respuesta porque una misma parte puede hidratarse de dos sitios a la vez: el
/// nombre del RUNT y el domicilio del histórico de actores, por ejemplo.
/// </summary>
/// <param name="Key">Variable del anexo normativo (<c>placa</c>, <c>marca</c>, <c>razon_social</c>, …).</param>
/// <param name="Value">Valor consultado. Nunca se emite un campo con valor vacío.</param>
/// <param name="Source">Una de <see cref="PrefillSources"/>.</param>
public sealed record PrefilledField(string Key, string? Value, string Source);

/// <summary>Fuente efectiva de un campo prellenado. Contrato estable con el frontend (HU-10).</summary>
public static class PrefillSources
{
    /// <summary>Cadena de proveedores RUNT (vehículo por placa o persona por documento).</summary>
    public const string Runt = "RUNT";

    /// <summary>Directorio de representantes legales de la compañía (<c>admin.company_legal_representatives</c>).</summary>
    public const string Directorio = "DIRECTORIO";

    /// <summary>Registro Único Empresarial y Social, como respaldo del directorio.</summary>
    public const string Rues = "RUES";

    /// <summary>Histórico de actores del tenant (<c>contact-lookup</c>), respaldo de contacto.</summary>
    public const string ContactLookup = "CONTACT_LOOKUP";

    /// <summary>Calculado por FLIT, no consultado: el dígito de verificación del NIT.</summary>
    public const string Calculado = "CALCULADO";
}

/// <summary>Cómo terminó el intento contra una fuente. Permite ver en la respuesta que una fuente se cayó sin que eso invalide el resto.</summary>
public static class PrefillOutcomes
{
    public const string Found = "found";
    public const string NotFound = "not_found";
    public const string Error = "error";
}

/// <summary>Traza de un intento contra una fuente, en el orden en que se intentó.</summary>
public sealed record PrefillSourceAttempt(string Source, string Outcome, string? ErrorCode = null);

/// <summary>
/// Respuesta común de los tres endpoints de prellenado.
///
/// <para><b>Degrada, no falla.</b> Sin coincidencia en ninguna fuente el resultado es
/// <c>Found = false</c> con <c>Fields</c> vacío y <c>Error = null</c>: el endpoint responde 200, no
/// 404. Solo cuando <b>todas</b> las fuentes consultadas fallaron por transporte queda un
/// <c>Error</c> normalizado.</para>
///
/// <para><b>No persiste nada.</b> Estos valores viajan únicamente hacia el formulario: ningún
/// handler de prellenado recibe repositorio ni storage, de modo que no puede escribir una fila en
/// <c>admin.standalone_documents</c> aunque alguien lo intentara.</para>
/// </summary>
/// <param name="Found">¿Alguna fuente devolvió datos?</param>
/// <param name="Source">Fuente efectiva principal (la que resolvió la identidad). <c>null</c> si no hubo.</param>
/// <param name="Fields">Campos hidratados, cada uno con su propia fuente.</param>
/// <param name="Attempts">Intentos por fuente, en orden de precedencia.</param>
/// <param name="Error"><c>invalid_request</c> o <c>provider_unavailable</c>; <c>null</c> en el camino normal.</param>
public sealed record PrefillResult(
    bool Found,
    string? Source,
    IReadOnlyList<PrefilledField> Fields,
    IReadOnlyList<PrefillSourceAttempt> Attempts,
    string? Error)
{
    public static PrefillResult Invalid() =>
        new(false, null, [], [], "invalid_request");

    public static PrefillResult Empty(IReadOnlyList<PrefillSourceAttempt> attempts) =>
        new(false, null, [], attempts, null);

    public static PrefillResult Unavailable(IReadOnlyList<PrefillSourceAttempt> attempts) =>
        new(false, null, [], attempts, "provider_unavailable");
}

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Documento personalizado de compañía RESUELTO para sustituir uno de los generados por el sistema
/// (HU #11316, Feature #11309, ADR-0042). <c>Tipo</c> es el MISMO vocabulario del <c>Tipo</c> del
/// adjunto (<c>mandato</c> / <c>tramite_virtual</c>): no cambia, así que pie, matriz documental y
/// orden del expediente se heredan gratis (DT-1/DT-2). <c>PersonalizedDocumentId</c>/<c>Version</c>
/// trazan exactamente qué versión de <c>admin.company_personalized_documents</c> entró al expediente.
/// </summary>
public sealed record ResolvedPersonalizedDocument(
    string Tipo,
    string Filename,
    byte[] Content,
    Guid PersonalizedDocumentId,
    int Version,
    string Sha256,
    int PageCount);

/// <summary>
/// Un tipo cuya versión activa no se pudo leer/abrir al momento de generar (DT-6, aislamiento del
/// fallo): el pipeline emite en su lugar el documento del sistema y registra este hecho SIN datos
/// personales. <c>Motivo</c> es un código corto (p. ej. <c>storage_no_disponible</c>, <c>pdf_ilegible</c>).
/// </summary>
public sealed record PersonalizedDocumentUnavailable(string Tipo, Guid PersonalizedDocumentId, string Motivo);

/// <summary>Desenlace de <see cref="IPersonalizedDocumentResolver.ResolveAsync"/>.</summary>
public sealed record PersonalizedDocumentResolution(
    IReadOnlyList<ResolvedPersonalizedDocument> Resolved,
    IReadOnlyList<PersonalizedDocumentUnavailable> Unavailable)
{
    public static readonly PersonalizedDocumentResolution Empty = new([], []);
}

/// <summary>
/// Puerto ÚNICO de sustitución documental por compañía (DT-1b del plan técnico): dado el tenant y los
/// tipos YA presentes en la lista `generated` de <c>GenerarFurHandler</c> (la aplicabilidad de cada
/// documento la decidió la lógica de siempre, antes de llegar aquí), devuelve qué tipos tienen una
/// versión activa personalizada lista para sustituir y cuáles fallaron al intentar leerse (para caer al
/// documento del sistema sin romper el expediente). La implementación (Infrastructure) cruza el módulo
/// Admin (custodia de versiones, HU #11313/#11314) con el storage, sin acoplar los módulos entre sí.
/// </summary>
public interface IPersonalizedDocumentResolver
{
    Task<PersonalizedDocumentResolution> ResolveAsync(
        Guid tenantId,
        IEnumerable<string> tipos,
        CancellationToken ct = default);
}

/// <summary>
/// Implementación nula (nunca resuelve). Default seguro para tests/DI que no ejercitan la
/// personalización, espejo de <c>NullProcedureDeedResolver</c>. Es también el comportamiento de
/// PRODUCCIÓN mientras la lista de tipos habilitados esté vacía (mandato → HU #11317, tramite_virtual →
/// HU #11318): el mecanismo existe, pero nada se sustituye todavía.
/// </summary>
public sealed class NullPersonalizedDocumentResolver : IPersonalizedDocumentResolver
{
    public static readonly NullPersonalizedDocumentResolver Instance = new();

    private NullPersonalizedDocumentResolver() { }

    public Task<PersonalizedDocumentResolution> ResolveAsync(
        Guid tenantId,
        IEnumerable<string> tipos,
        CancellationToken ct = default) =>
        Task.FromResult(PersonalizedDocumentResolution.Empty);
}

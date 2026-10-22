namespace Flit.Admin.Application.Plataforma.Mandatos;

/// <summary>
/// Formato de contrato de mandato tal como lo ve el Super Admin (HU #13168 y #13171): lo fijo del catálogo del
/// sistema (código, redacción base) junto con lo editable (nombre, tipo, versión de plantilla).
/// </summary>
/// <param name="Code">Código estable (<c>template_code</c>).</param>
/// <param name="Name">Nombre visible vigente (el de fábrica hasta que el Super Admin lo cambie).</param>
/// <param name="AssignmentMode">Tipo de mandato asociado: <c>signer</c> | <c>institutional</c> | <c>open</c>.</param>
/// <param name="BaseRedaction">Redacción base del generador; null para <c>auto</c>, que solo delega.</param>
/// <param name="SelectableAsRedaction">False para <c>auto</c>: no es una redacción previsualizable ni editable.</param>
/// <param name="CurrentVersion">Última versión de plantilla publicada; 0 = rige la redacción del generador.</param>
/// <param name="RowVersion">Token de concurrencia que el cliente devuelve al guardar; null si el formato no tiene fila.</param>
public sealed record MandateFormatView(
    string Code,
    string Name,
    string AssignmentMode,
    string? BaseRedaction,
    bool SelectableAsRedaction,
    int CurrentVersion,
    long? RowVersion,
    DateTimeOffset? UpdatedAt)
{
    public bool DelegatesToOfficeTemplate => !SelectableAsRedaction;

    public bool HasCustomTemplate => CurrentVersion > 0;
}

/// <summary>Metadatos de una versión publicada (sin el cuerpo).</summary>
public sealed record MandateFormatVersionInfo(
    int VersionNumber,
    string BodySha256,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy);

/// <summary>Detalle de un formato: la vista más el cuerpo de la versión vigente y el historial.</summary>
public sealed record MandateFormatDetailView(
    MandateFormatView Format,
    string? CurrentBody,
    IReadOnlyList<MandateFormatVersionInfo> Versions);

/// <summary>
/// Cambios pedidos sobre un formato. Nulo = no tocar ese campo. <see cref="RowVersion"/> es obligatorio: ausente o
/// desactualizado responde conflicto.
/// </summary>
public sealed record UpdateMandateFormatRequest(
    long? RowVersion,
    string? Name,
    string? AssignmentMode,
    string? Body);

/// <summary>Variable <c>{{...}}</c> desconocida en el cuerpo, con su posición (línea y columna desde 1).</summary>
public sealed record MandateFormatUnknownVariable(string Name, int Line, int Column);

public enum MandateFormatUpdateStatus
{
    Ok,

    /// <summary>El código no está en el catálogo (no se crean formatos).</summary>
    NotFound,

    /// <summary>Validación fallida: ver <see cref="MandateFormatUpdateResult.ErrorCode"/>.</summary>
    BadRequest,

    /// <summary>RowVersion ausente o desactualizado.</summary>
    Conflict,
}

/// <summary>Resultado de <see cref="IMandateFormatAdminService.UpdateAsync"/>.</summary>
/// <param name="ErrorCode">
/// Código estable del motivo cuando no es Ok: <c>nombre_vacio</c>, <c>nombre_demasiado_largo</c>,
/// <c>nombre_repetido</c>, <c>assignment_mode_invalido</c>, <c>formato_sin_plantilla</c>, <c>plantilla_vacia</c>,
/// <c>plantilla_demasiado_larga</c>, <c>plantilla_sintaxis_invalida</c>, <c>plantilla_variable_invalida</c>,
/// <c>template_code_invalido</c> o <c>row_version_conflict</c>.
/// </param>
/// <param name="Previous">Estado antes del cambio (null si no se pudo leer).</param>
/// <param name="Current">Estado después (igual a Previous si no hubo cambio o falló).</param>
/// <param name="PublishedVersion">Número de la versión creada, o null si no se publicó plantilla.</param>
/// <param name="Changed">False si los valores pedidos ya eran los vigentes.</param>
public sealed record MandateFormatUpdateResult(
    MandateFormatUpdateStatus Status,
    string? ErrorCode = null,
    MandateFormatView? Previous = null,
    MandateFormatView? Current = null,
    int? PublishedVersion = null,
    bool Changed = false,
    IReadOnlyList<MandateFormatUnknownVariable>? UnknownVariables = null);

/// <summary>
/// Edición de los formatos de contrato de mandato por el Super Admin (HU #13171, Feature #13118): nombre visible,
/// tipo asociado y plantilla versionada de cada formato EXISTENTE. No crea ni elimina formatos.
/// </summary>
public interface IMandateFormatAdminService
{
    /// <summary>Todos los formatos del catálogo con sus valores vigentes, en el orden del catálogo.</summary>
    Task<IReadOnlyList<MandateFormatView>> ListAsync(CancellationToken ct = default);

    /// <summary>Un formato con el cuerpo de su versión vigente y el historial; null si el código no existe.</summary>
    Task<MandateFormatDetailView?> GetAsync(string code, CancellationToken ct = default);

    /// <summary>Cuerpo y metadatos de una versión concreta; null si no existe.</summary>
    Task<(MandateFormatVersionInfo Info, string Body)?> GetVersionAsync(
        string code, int versionNumber, CancellationToken ct = default);

    Task<MandateFormatUpdateResult> UpdateAsync(
        string code,
        UpdateMandateFormatRequest request,
        Guid? userId,
        CancellationToken ct = default);

    /// <summary>
    /// «Restablecer redacción de fábrica»: descarta la plantilla editada por el Super Admin y el formato vuelve al texto
    /// original del sistema. Conserva el nombre, el tipo y el historial de versiones. <c>formato_sin_plantilla</c> si el
    /// formato no tiene redacción propia.
    /// </summary>
    Task<MandateFormatUpdateResult> ResetTemplateAsync(
        string code,
        long? rowVersion,
        Guid? userId,
        CancellationToken ct = default);
}

namespace Flit.Admin.Application.Plataforma.Mandatos;

/// <summary>Personalización vigente de un formato de contrato de mandato (HU #13169).</summary>
/// <param name="Code">Código del catálogo (<c>template_code</c>).</param>
/// <param name="Name">Nombre visible.</param>
/// <param name="AssignmentMode"><c>signer</c> | <c>institutional</c> | <c>open</c>.</param>
/// <param name="CurrentVersion">Última versión publicada; 0 = sin plantilla personalizada.</param>
/// <param name="RowVersion">Token de concurrencia optimista.</param>
public sealed record MandateFormatSettingView(
    string Code,
    string Name,
    string AssignmentMode,
    int CurrentVersion,
    long RowVersion,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy);

/// <summary>Versión inmutable de la plantilla de un formato.</summary>
public sealed record MandateFormatVersionView(
    string Code,
    int VersionNumber,
    string Body,
    string BodySha256,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy);

/// <summary>
/// Cambios a guardar sobre un formato; cada campo nulo se deja como está. Un <see cref="Body"/> no nulo publica una
/// versión nueva. <see cref="ExpectedRowVersion"/> es obligatorio: ausente o desactualizado ⇒ conflicto.
/// </summary>
public sealed record SaveMandateFormatCommand(
    long? ExpectedRowVersion,
    string? Name = null,
    string? AssignmentMode = null,
    string? Body = null);

public enum MandateFormatWriteStatus
{
    Ok,

    /// <summary>El código no está en el catálogo o no tiene fila de configuración: no se crea nada.</summary>
    UnknownCode,

    /// <summary>RowVersion ausente o desactualizado, o la versión nueva chocó con otra publicada a la vez.</summary>
    Conflict,

    /// <summary>Cuerpo vacío o de más de 100000 caracteres (mismo límite que el editor de plantilla del organismo).</summary>
    InvalidBody,
}

/// <summary>
/// Resultado de guardar: <see cref="PublishedVersion"/> trae la versión creada si el comando incluía cuerpo, y
/// <see cref="Changed"/> es false si no cambió nada real (mismos valores y sin cuerpo).
/// </summary>
public sealed record MandateFormatSaveResult(
    MandateFormatWriteStatus Status,
    MandateFormatSettingView? Setting = null,
    MandateFormatVersionView? PublishedVersion = null,
    bool Changed = false);

/// <summary>Persistencia de la personalización y las versiones de los formatos de mandato (HU #13169).</summary>
public interface IMandateFormatRepository
{
    /// <summary>Todos los formatos con fila de configuración, en el orden del catálogo.</summary>
    Task<IReadOnlyList<MandateFormatSettingView>> ListAsync(CancellationToken ct = default);

    Task<MandateFormatSettingView?> GetAsync(string code, CancellationToken ct = default);

    /// <summary>Versión vigente (la última publicada), o null si el formato no tiene plantilla personalizada.</summary>
    Task<MandateFormatVersionView?> GetCurrentVersionAsync(string code, CancellationToken ct = default);

    /// <summary>Versión anterior o actual por número, o null si no existe.</summary>
    Task<MandateFormatVersionView?> GetVersionAsync(string code, int versionNumber, CancellationToken ct = default);

    Task<IReadOnlyList<MandateFormatVersionView>> ListVersionsAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Guarda nombre, tipo y/o plantilla en una sola unidad: o se aplica todo o nada. Crea la versión siguiente
    /// (numerada, con SHA-256, autor y fecha) si hay cuerpo. No valida variables ni nombres: eso es del servicio.
    /// </summary>
    Task<MandateFormatSaveResult> SaveAsync(
        string code,
        SaveMandateFormatCommand command,
        Guid? userId,
        CancellationToken ct = default);
}

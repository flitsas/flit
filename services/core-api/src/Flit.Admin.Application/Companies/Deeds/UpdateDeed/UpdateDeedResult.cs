namespace Flit.Admin.Application.Companies.Deeds.UpdateDeed;

/// <summary>Desenlace de la edición de una escritura.</summary>
public enum UpdateDeedOutcome
{
    /// <summary>La escritura se actualizó.</summary>
    Updated,

    /// <summary>No existe una escritura con ese id en el tenant.</summary>
    NotFound,

    /// <summary>Los metadatos no pasaron validación (422).</summary>
    Invalid,
}

/// <summary>
/// Resultado de la edición: actualizado (con <see cref="DeedUploadTicket"/> si se reemplazó el PDF),
/// no encontrado (404) o inválido (422 + errores).
/// </summary>
public sealed class UpdateDeedResult
{
    private UpdateDeedResult(
        UpdateDeedOutcome outcome,
        DeedUploadTicket? upload,
        IReadOnlyList<DeedValidationError> errors)
    {
        Outcome = outcome;
        Upload = upload;
        Errors = errors;
    }

    public UpdateDeedOutcome Outcome { get; }

    /// <summary>Ticket de subida si la edición reemplazó el PDF; <c>null</c> si conservó el artefacto.</summary>
    public DeedUploadTicket? Upload { get; }

    public IReadOnlyList<DeedValidationError> Errors { get; }

    public static UpdateDeedResult Updated(DeedUploadTicket? upload) =>
        new(UpdateDeedOutcome.Updated, upload, []);

    public static UpdateDeedResult NotFound() =>
        new(UpdateDeedOutcome.NotFound, null, []);

    public static UpdateDeedResult Invalid(IReadOnlyList<DeedValidationError> errors) =>
        new(UpdateDeedOutcome.Invalid, null, errors);
}

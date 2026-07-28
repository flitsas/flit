namespace Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;

/// <summary>Resultado del alta: válido (con id + huella generada) o inválido (422 + errores).</summary>
public sealed class CreateMandateSignerResult
{
    private CreateMandateSignerResult(
        bool isValid,
        Guid? mandateSignerId,
        string? integrityHash,
        IReadOnlyList<MandateSignerValidationError> errors,
        MandateSignerIdentityOutcome identity = MandateSignerIdentityOutcome.NotAttempted)
    {
        IsValid = isValid;
        MandateSignerId = mandateSignerId;
        IntegrityHash = integrityHash;
        Errors = errors;
        Identity = identity;
    }

    public bool IsValid { get; }
    public Guid? MandateSignerId { get; }
    public string? IntegrityHash { get; }
    public IReadOnlyList<MandateSignerValidationError> Errors { get; }

    /// <summary>
    /// Desenlace de la validación de identidad disparada por el alta (HU #11000). El alta NUNCA falla por
    /// esto (best-effort); el desenlace viaja al cliente para que el aviso al usuario sea veraz.
    /// </summary>
    public MandateSignerIdentityOutcome Identity { get; }

    public static CreateMandateSignerResult Success(
        Guid mandateSignerId,
        string integrityHash,
        MandateSignerIdentityOutcome identity = MandateSignerIdentityOutcome.NotAttempted) =>
        new(true, mandateSignerId, integrityHash, [], identity);

    public static CreateMandateSignerResult Invalid(IReadOnlyList<MandateSignerValidationError> errors) =>
        new(false, null, null, errors);
}

/// <summary>Desenlace de la validación de identidad al dar de alta un mandatario (HU #11000).</summary>
public enum MandateSignerIdentityOutcome
{
    /// <summary>No se intentó: el mandatario se registró sin correo.</summary>
    NotAttempted,

    /// <summary>Se envió una validación nueva al correo del mandatario.</summary>
    Sent,

    /// <summary>Ya tenía una validación aprobada y vigente: se reutiliza (no se envía correo).</summary>
    Reused,

    /// <summary>El proveedor de identidad falló: el mandatario quedó creado y el reenvío queda disponible.</summary>
    Failed,
}

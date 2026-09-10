namespace Flit.Tramites.Domain.RuntConfirmation;

/// <summary>
/// Veredicto de un intento de confirmación. Solo <see cref="Confirmed"/> marca el trámite como
/// confirmado; <see cref="Error"/> NO es un veredicto de negocio (no consume intento ni cambia marca):
/// es el defecto de v1 que no se hereda —un timeout del proveedor nunca puede confirmar ni negar nada.
/// </summary>
public enum RuntConfirmationVerdict
{
    Confirmed,
    Pending,
    Discrepancy,
    Unverifiable,
    Error,
}

public static class RuntConfirmationVerdictCodes
{
    public const string Confirmed = "confirmed";
    public const string Pending = "pending";
    public const string Discrepancy = "discrepancy";
    public const string Unverifiable = "unverifiable";
    public const string Error = "error";

    public static readonly IReadOnlyList<string> All = [Confirmed, Pending, Discrepancy, Unverifiable, Error];

    public static string ToCode(RuntConfirmationVerdict verdict) => verdict switch
    {
        RuntConfirmationVerdict.Confirmed => Confirmed,
        RuntConfirmationVerdict.Pending => Pending,
        RuntConfirmationVerdict.Discrepancy => Discrepancy,
        RuntConfirmationVerdict.Unverifiable => Unverifiable,
        RuntConfirmationVerdict.Error => Error,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Veredicto no soportado."),
    };

    public static RuntConfirmationVerdict? FromCode(string? code) => code switch
    {
        Confirmed => RuntConfirmationVerdict.Confirmed,
        Pending => RuntConfirmationVerdict.Pending,
        Discrepancy => RuntConfirmationVerdict.Discrepancy,
        Unverifiable => RuntConfirmationVerdict.Unverifiable,
        Error => RuntConfirmationVerdict.Error,
        _ => null,
    };
}

/// <summary>
/// Marca operativa de <c>procedure_instances.runt_flag</c>. Es interna: la columna «Confirmado en
/// RUNT» del gestor solo muestra SÍ / NO / —, nunca esta marca (decisión de producto).
/// </summary>
public static class RuntConfirmationFlags
{
    /// <summary>RECHAZADA en el RUNT, o <c>discrepancyAfterRuns</c> intentos en Pendiente.</summary>
    public const string Discrepancia = "discrepancia";

    /// <summary>El RUNT no expone solicitudes para el vehículo o el tipo no tiene equivalente: no se vuelve a consultar.</summary>
    public const string NoVerificable = "no_verificable";

    /// <summary>Se alcanzó <c>maxAttempts</c> sin confirmar: sale del universo.</summary>
    public const string Tope = "tope";

    public static readonly IReadOnlyList<string> All = [Discrepancia, NoVerificable, Tope];
}

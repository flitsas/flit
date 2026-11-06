namespace Flit.Tramites.Domain.Entities;

/// <summary>HU #14310 — estados de la validación en el dispositivo de una foto de la captura manual (lista cerrada).</summary>
public static class ManualPhotoCheckStates
{
    public const string Verificada = "verificada";
    public const string NoVerificada = "no_verificada";

    public static bool IsValid(string? value) => value is Verificada or NoVerificada;
}

/// <summary>HU #14310 — motivos de una foto no verificada (lista cerrada; nunca texto libre del cliente).</summary>
public static class ManualPhotoCheckReasons
{
    public const string SinRostro = "sin_rostro";
    public const string VariosRostros = "varios_rostros";
    public const string SinDocumento = "sin_documento";
    public const string LadoIncorrecto = "lado_incorrecto";
    public const string SinSoporte = "sin_soporte";
    public const string SinResultado = "sin_resultado";

    public static readonly IReadOnlyList<string> Todos =
        [SinRostro, VariosRostros, SinDocumento, LadoIncorrecto, SinSoporte, SinResultado];

    public static bool IsValid(string? value) => value is not null && Todos.Contains(value);
}

/// <summary>
/// HU #14310 — resultado informativo de la validación de UNA foto (no es un control de seguridad). Invariante: <c>verificada</c>
/// sin motivo; <c>no_verificada</c> siempre con motivo de la lista cerrada.
/// </summary>
public sealed record ManualPhotoCheck
{
    private ManualPhotoCheck(string state, string? reason)
    {
        State = state;
        Reason = reason;
    }

    public string State { get; }

    public string? Reason { get; }

    public static ManualPhotoCheck Verified { get; } = new(ManualPhotoCheckStates.Verificada, null);

    /// <summary>Foto sin resultado utilizable (campo ausente, JSON inválido, valor desconocido).</summary>
    public static ManualPhotoCheck Unknown { get; } = NotVerified(ManualPhotoCheckReasons.SinResultado);

    public static ManualPhotoCheck NotVerified(string reason)
    {
        if (!ManualPhotoCheckReasons.IsValid(reason))
            throw new ArgumentException("El motivo no está en la lista cerrada.", nameof(reason));
        return new ManualPhotoCheck(ManualPhotoCheckStates.NoVerificada, reason);
    }

    /// <summary>Reconstruye desde columnas; cualquier combinación fuera de la lista cerrada se lee como «sin resultado».</summary>
    public static ManualPhotoCheck? FromColumns(string? state, string? reason)
    {
        if (state is null)
            return null;
        if (state == ManualPhotoCheckStates.Verificada)
            return Verified;
        return state == ManualPhotoCheckStates.NoVerificada && ManualPhotoCheckReasons.IsValid(reason)
            ? NotVerified(reason!)
            : Unknown;
    }
}

/// <summary>HU #14310 — resultado de las tres fotos de una captura.</summary>
public sealed record ManualPhotoChecks(ManualPhotoCheck Face, ManualPhotoCheck IdFront, ManualPhotoCheck IdBack)
{
    /// <summary>Envío sin el campo: las tres quedan «no verificada / sin resultado».</summary>
    public static ManualPhotoChecks AllUnknown { get; } =
        new(ManualPhotoCheck.Unknown, ManualPhotoCheck.Unknown, ManualPhotoCheck.Unknown);
}

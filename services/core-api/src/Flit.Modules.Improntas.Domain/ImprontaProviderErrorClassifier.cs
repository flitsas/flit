namespace Flit.Modules.Improntas.Domain;

/// <summary>Clasificación de un <see cref="ImprontaRuntException"/> para que cada caller decida su propio status HTTP.</summary>
public enum ImprontaProviderErrorKind
{
    /// <summary>Datos inválidos ante el proveedor (422/400) — no transitorio.</summary>
    Validation,

    /// <summary>Proveedor rechazó la autenticación (401) — no transitorio.</summary>
    Unauthorized,

    /// <summary>Proveedor no disponible temporalmente (502) — transitorio, reintentable.</summary>
    Unavailable,
}

/// <summary>
/// Clasifica errores de Kyverum RUNT (compartido entre el módulo admin y el flujo integrado de
/// Trámites). <see cref="ImprontaRuntException"/> no expone el código de error crudo del proveedor
/// (solo el mensaje, ya traducido a español, y <see cref="ImprontaRuntException.IsTransient"/> —
/// ver <c>ImprontaRuntClient.BuildErrorAsync</c>), así que la distinción entre validación y
/// autenticación se hace inspeccionando prefijos estables del mensaje.
/// </summary>
public static class ImprontaProviderErrorClassifier
{
    /// <summary>Prefijo que <c>ImprontaRuntClient.BuildValidationMessage</c> arma para <c>VALIDATION_ERROR</c>.</summary>
    private const string ValidationMessagePrefix = "Datos de la impronta inválidos:";

    /// <summary>
    /// Prefijo que <c>ImprontaRuntClient</c> arma cuando Kyverum responde 200 con <c>ok:false</c> (no es
    /// un error HTTP, es un motivo de negocio). Verificado contra el proveedor real: cuando el request
    /// envía placa+documento y no corresponden al propietario activo del vehículo en el RUNT, la
    /// respuesta es <c>ok:false</c> de forma consistente y reproducible (no intermitente) — es un
    /// rechazo de datos, no una falla de disponibilidad, así que se clasifica igual que
    /// <see cref="ValidationMessagePrefix"/> y no como <see cref="ImprontaProviderErrorKind.Unavailable"/>.
    /// </summary>
    private const string UnavailableForRequestMessagePrefix = "Impronta no disponible:";

    public static ImprontaProviderErrorKind Classify(ImprontaRuntException ex)
    {
        if (ex.IsTransient)
            return ImprontaProviderErrorKind.Unavailable;

        return ex.Message.StartsWith(ValidationMessagePrefix, StringComparison.Ordinal)
            || ex.Message.StartsWith(UnavailableForRequestMessagePrefix, StringComparison.Ordinal)
            ? ImprontaProviderErrorKind.Validation
            : ImprontaProviderErrorKind.Unauthorized;
    }
}

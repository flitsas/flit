using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure;

/// <summary>
/// HU #10970 — logging de alto rendimiento (source-generated) del resultado de resolver la sección
/// <c>TramiteValidations</c> al arrancar. Delegados <c>LoggerMessage</c> para cumplir CA1848/CA1873.
/// </summary>
internal static partial class TramiteValidationLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "TramiteValidations:{Validacion}:Mode tiene el valor no reconocido '{Valor}'. " +
                  "Se aplica 'block' (bloqueo duro). Valores válidos: block, warn, off.")]
    public static partial void UnrecognizedMode(ILogger logger, string validacion, string? valor);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "TramiteValidations resuelto — DuplicateActiveProcedure={Duplicidad}, " +
                  "VehicleRegistrationState={Registral}.")]
    public static partial void PolicyResolved(
        ILogger logger,
        TramiteValidationMode duplicidad,
        TramiteValidationMode registral);
}

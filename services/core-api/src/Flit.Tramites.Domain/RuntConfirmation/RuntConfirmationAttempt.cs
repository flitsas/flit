namespace Flit.Tramites.Domain.RuntConfirmation;

/// <summary>
/// Un intento de confirmación de un trámite (<c>tramites.runt_confirmation_attempts</c>). Es la fila
/// del Historial: qué se consultó, con qué proveedor, qué respondió el RUNT (crudo referenciado, no
/// copiado) y por qué se decidió lo que se decidió. El motivo legible ES el log; un booleano con
/// fecha no le sirve a nadie para entender un NO.
/// </summary>
public sealed class RuntConfirmationAttempt
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>Corrida a la que pertenece; NULL para un «Consultar ahora» o una re-evaluación.</summary>
    public Guid? RunId { get; set; }

    /// <summary>Número de intento del trámite (1..n). Una re-evaluación no consume número: hereda el del intento origen.</summary>
    public int AttemptNo { get; set; }

    public DateTimeOffset QueriedAt { get; set; }

    /// <summary>Proveedor usado en ESTE intento. Cambiar la configuración después no lo reescribe (AC5).</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>Cómo se consultó: <see cref="RuntConfirmationQueryKinds"/>.</summary>
    public string QueryKind { get; set; } = string.Empty;

    public string Verdict { get; set; } = RuntConfirmationVerdictCodes.Pending;
    public string ReasonText { get; set; } = string.Empty;
    public string RuleVersion { get; set; } = string.Empty;

    /// <summary>Respuesta cruda principal en <c>tramites.external_query_payloads</c> (VIN, o placa + documento del comprador/propietario).</summary>
    public Guid? RawPayloadId { get; set; }

    /// <summary>Segunda respuesta cruda: en traspaso la del vendedor; en <c>vin_plate</c> la del desempate placa + propietario.</summary>
    public Guid? SellerRawPayloadId { get; set; }

    /// <summary>Usuario que pidió un «Consultar ahora»; NULL en la corrida programada.</summary>
    public Guid? RequestedBy { get; set; }

    /// <summary>Intento cuyo crudo se reutilizó para una re-evaluación sin volver a pagar (AC12 de HU #12308).</summary>
    public Guid? ReevaluatedFromAttemptId { get; set; }

    /// <summary>Marca de <c>runt_flag</c> que dejó este intento en el trámite, si alguna.</summary>
    public string? FlagApplied { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public static class RuntConfirmationQueryKinds
{
    public const string Vin = "vin";
    public const string Plate = "plate";
    public const string PlatePair = "plate_pair";
    /// <summary>Matrícula: VIN y, como desempate, placa + documento del propietario (dos llamadas).</summary>
    public const string VinPlate = "vin_plate";
    /// <summary>Re-evaluación desde el crudo guardado: no hubo llamada al proveedor.</summary>
    public const string Reevaluation = "reevaluation";

    public static readonly IReadOnlyList<string> All = [Vin, Plate, PlatePair, VinPlate, Reevaluation];
}

namespace Flit.Admin.Application.ProcedureInstances.CreateProcedureInstance;

/// <summary>Desenlace del alta de un trámite (AC1/AC3).</summary>
public enum CreateProcedureInstanceOutcome
{
    /// <summary>Trámite y snapshot creados → HTTP 201.</summary>
    Created,

    /// <summary>Datos inválidos o número de referencia duplicado → HTTP 422.</summary>
    ValidationFailed,

    /// <summary>El tipo de trámite no existe → HTTP 404.</summary>
    ProcedureTypeNotFound,

    /// <summary>El tenant (cliente) no existe → HTTP 404.</summary>
    TenantNotFound,

    /// <summary>Falló la persistencia del trámite o del snapshot; rollback → HTTP 500.</summary>
    Failed,
}

/// <summary>
/// Resultado del alta: el <see cref="Outcome"/> determina el status HTTP;
/// <see cref="Response"/> trae el trámite creado (201) y <see cref="Error"/> el mensaje
/// en 422 / 500.
/// </summary>
public sealed class CreateProcedureInstanceResult
{
    private CreateProcedureInstanceResult(
        CreateProcedureInstanceOutcome outcome,
        ProcedureInstanceResponse? response,
        string? error)
    {
        Outcome = outcome;
        Response = response;
        Error = error;
    }

    public CreateProcedureInstanceOutcome Outcome { get; }

    public ProcedureInstanceResponse? Response { get; }

    public string? Error { get; }

    public static CreateProcedureInstanceResult Created(ProcedureInstanceResponse response) =>
        new(CreateProcedureInstanceOutcome.Created, response, null);

    public static CreateProcedureInstanceResult ValidationFailed(string error) =>
        new(CreateProcedureInstanceOutcome.ValidationFailed, null, error);

    public static CreateProcedureInstanceResult ProcedureTypeNotFound { get; } =
        new(CreateProcedureInstanceOutcome.ProcedureTypeNotFound, null, null);

    public static CreateProcedureInstanceResult TenantNotFound { get; } =
        new(CreateProcedureInstanceOutcome.TenantNotFound, null, null);

    public static CreateProcedureInstanceResult Failed(string error) =>
        new(CreateProcedureInstanceOutcome.Failed, null, error);
}

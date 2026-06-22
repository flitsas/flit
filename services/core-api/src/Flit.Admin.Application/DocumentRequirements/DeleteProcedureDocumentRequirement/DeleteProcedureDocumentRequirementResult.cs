namespace Flit.Admin.Application.DocumentRequirements.DeleteProcedureDocumentRequirement;

/// <summary>Desenlace del borrado de una asociación (AC4).</summary>
public enum DeleteProcedureDocumentRequirementOutcome
{
    /// <summary>Eliminada físicamente → HTTP 204.</summary>
    Deleted,

    /// <summary>El id no existe → HTTP 404.</summary>
    NotFound,

    /// <summary>En uso en trámites activos (guard) → HTTP 409.</summary>
    InUse,
}

/// <summary>Resultado del borrado; el <see cref="Outcome"/> determina el status HTTP.</summary>
public sealed class DeleteProcedureDocumentRequirementResult
{
    /// <summary>Mensaje 409 cuando la asociación está en uso.</summary>
    public const string InUseMessage =
        "La asociación está en uso en trámites activos y no puede eliminarse";

    private DeleteProcedureDocumentRequirementResult(DeleteProcedureDocumentRequirementOutcome outcome) =>
        Outcome = outcome;

    public DeleteProcedureDocumentRequirementOutcome Outcome { get; }

    public static DeleteProcedureDocumentRequirementResult Deleted { get; } =
        new(DeleteProcedureDocumentRequirementOutcome.Deleted);

    public static DeleteProcedureDocumentRequirementResult NotFound { get; } =
        new(DeleteProcedureDocumentRequirementOutcome.NotFound);

    public static DeleteProcedureDocumentRequirementResult InUse { get; } =
        new(DeleteProcedureDocumentRequirementOutcome.InUse);
}

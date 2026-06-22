namespace Flit.Admin.Application.DocumentRequirements.CreateProcedureDocumentRequirement;

/// <summary>
/// Comando de alta de una asociación (AC1). Combina el payload con la identidad del
/// SuperAdmin (claim <c>sub</c>) que ejecuta el alta (<c>created_by</c>).
/// </summary>
public sealed class CreateProcedureDocumentRequirementCommand
{
    public required CreateProcedureDocumentRequirementRequest Request { get; init; }

    /// <summary>Id del usuario que crea (claim <c>sub</c> del JWT). Opcional.</summary>
    public Guid? CreatedBy { get; init; }
}

namespace Flit.Admin.Application.DocumentRequirements.UpdateProcedureDocumentRequirement;

/// <summary>Comando de actualización de una asociación (AC3): id de ruta + payload.</summary>
public sealed class UpdateProcedureDocumentRequirementCommand
{
    public required Guid Id { get; init; }

    public required UpdateProcedureDocumentRequirementRequest Request { get; init; }
}

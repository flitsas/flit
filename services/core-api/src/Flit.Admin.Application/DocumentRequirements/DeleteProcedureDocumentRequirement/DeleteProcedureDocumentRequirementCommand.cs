namespace Flit.Admin.Application.DocumentRequirements.DeleteProcedureDocumentRequirement;

/// <summary>Comando de borrado físico de una asociación (AC4).</summary>
public sealed class DeleteProcedureDocumentRequirementCommand
{
    public required Guid Id { get; init; }
}

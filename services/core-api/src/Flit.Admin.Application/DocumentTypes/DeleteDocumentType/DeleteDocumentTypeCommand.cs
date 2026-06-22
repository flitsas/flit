namespace Flit.Admin.Application.DocumentTypes.DeleteDocumentType;

/// <summary>
/// Comando de baja lógica (soft-delete) de un tipo de documento (AC4/AC6).
/// </summary>
public sealed class DeleteDocumentTypeCommand
{
    public required Guid Id { get; init; }

    /// <summary>Id del usuario que desactiva (claim <c>sub</c> del JWT). Opcional.</summary>
    public Guid? DeletedBy { get; init; }
}

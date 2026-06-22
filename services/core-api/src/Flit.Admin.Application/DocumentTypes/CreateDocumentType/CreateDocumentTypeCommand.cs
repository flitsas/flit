namespace Flit.Admin.Application.DocumentTypes.CreateDocumentType;

/// <summary>
/// Comando de alta de un tipo de documento (AC1). Combina el payload con la
/// identidad del SuperAdmin (claim <c>sub</c>) que ejecuta el alta (<c>created_by</c>).
/// </summary>
public sealed class CreateDocumentTypeCommand
{
    public required CreateDocumentTypeRequest Request { get; init; }

    /// <summary>Id del usuario que crea (claim <c>sub</c> del JWT). Opcional.</summary>
    public Guid? CreatedBy { get; init; }
}

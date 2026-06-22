namespace Flit.Admin.Application.DocumentTypes.UpdateDocumentType;

/// <summary>
/// Comando de actualización (AC3). Combina el id de ruta, el payload y la identidad
/// del SuperAdmin que ejecuta el cambio (<c>updated_by</c>).
/// </summary>
public sealed class UpdateDocumentTypeCommand
{
    public required Guid Id { get; init; }

    public required UpdateDocumentTypeRequest Request { get; init; }

    /// <summary>Id del usuario que actualiza (claim <c>sub</c> del JWT). Opcional.</summary>
    public Guid? UpdatedBy { get; init; }
}

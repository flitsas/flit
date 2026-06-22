namespace Flit.Admin.Application.DocumentTypes.ReactivateDocumentType;

/// <summary>
/// Comando de reactivación de un tipo de documento previamente desactivado
/// (vuelve <c>is_active = true</c>). Contraparte del soft-delete (AC4).
/// </summary>
public sealed class ReactivateDocumentTypeCommand
{
    public required Guid Id { get; init; }

    /// <summary>Id del usuario que reactiva (claim <c>sub</c> del JWT). Opcional.</summary>
    public Guid? UpdatedBy { get; init; }
}

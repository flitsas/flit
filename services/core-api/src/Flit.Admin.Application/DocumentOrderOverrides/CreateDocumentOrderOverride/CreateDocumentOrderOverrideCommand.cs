namespace Flit.Admin.Application.DocumentOrderOverrides.CreateDocumentOrderOverride;

/// <summary>
/// Comando de alta de un override (AC1/AC2). Combina el <c>scope</c> normalizado
/// (<c>OT</c> | <c>CLIENTE</c>), el payload y la identidad del SuperAdmin (claim
/// <c>sub</c>) que ejecuta el alta (<c>created_by</c>).
/// </summary>
public sealed class CreateDocumentOrderOverrideCommand
{
    /// <summary>Ámbito normalizado del override (<c>OT</c> | <c>CLIENTE</c>).</summary>
    public required string Scope { get; init; }

    public required CreateDocumentOrderOverrideRequest Request { get; init; }

    /// <summary>Id del usuario que crea (claim <c>sub</c> del JWT). Opcional.</summary>
    public Guid? CreatedBy { get; init; }
}

namespace Flit.Admin.Application.DocumentRequirementOverrides.SetDocumentRequirementOverride;

/// <summary>
/// Comando de upsert/limpiar de la obligatoriedad por OT (HU #10198). Combina el payload con
/// la identidad del SuperAdmin (claim <c>sub</c>) que ejecuta el cambio (created_by/updated_by).
/// </summary>
public sealed class SetDocumentRequirementOverrideCommand
{
    public required SetDocumentRequirementOverrideRequest Request { get; init; }

    /// <summary>Id del usuario que ejecuta (claim <c>sub</c> del JWT). Opcional.</summary>
    public Guid? Actor { get; init; }
}

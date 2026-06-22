namespace Flit.Admin.Application.DocumentOrderOverrides.UpdateDocumentOrderOverride;

/// <summary>
/// Payload del PUT de reordenamiento de un override (HU #10198). Solo el orden es mutable.
/// </summary>
/// <param name="Orden">Nuevo orden (smallint ≥ 0).</param>
public sealed record UpdateDocumentOrderOverrideRequest(int? Orden);

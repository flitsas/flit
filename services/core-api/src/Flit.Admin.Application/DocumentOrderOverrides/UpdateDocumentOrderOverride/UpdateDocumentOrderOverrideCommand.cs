namespace Flit.Admin.Application.DocumentOrderOverrides.UpdateDocumentOrderOverride;

/// <summary>
/// Comando de reordenamiento de un override (HU #10198). Solo el <c>orden</c> es mutable;
/// la tupla (trámite, documento, scope, referencia) no cambia. Lo usa el arrastre de la
/// lista de overrides, que persiste el nuevo <c>sort_order</c> de cada fila movida.
/// </summary>
public sealed class UpdateDocumentOrderOverrideCommand
{
    /// <summary>Id del override a reordenar.</summary>
    public required Guid Id { get; init; }

    /// <summary>Nuevo orden (smallint ≥ 0). Validado por <c>DocumentOrderOverrideValidator</c>.</summary>
    public int? Orden { get; init; }
}

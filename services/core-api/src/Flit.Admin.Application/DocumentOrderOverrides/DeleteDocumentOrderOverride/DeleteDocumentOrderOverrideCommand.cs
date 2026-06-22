namespace Flit.Admin.Application.DocumentOrderOverrides.DeleteDocumentOrderOverride;

/// <summary>Comando de borrado físico de un override (HU #10196, AC6).</summary>
public sealed class DeleteDocumentOrderOverrideCommand
{
    public required Guid Id { get; init; }
}

namespace Flit.Admin.Application.DocumentOrderOverrides.ListDocumentOrderOverrides;

/// <summary>
/// Resultado del listado de overrides. Serializado como <c>{ data }</c> ordenado por
/// <c>orden</c> ascendente (HU #10196, AC5).
/// </summary>
public sealed class ListDocumentOrderOverridesResult
{
    public ListDocumentOrderOverridesResult(IReadOnlyList<DocumentOrderOverrideResponse> data)
    {
        Data = data;
    }

    public IReadOnlyList<DocumentOrderOverrideResponse> Data { get; }
}

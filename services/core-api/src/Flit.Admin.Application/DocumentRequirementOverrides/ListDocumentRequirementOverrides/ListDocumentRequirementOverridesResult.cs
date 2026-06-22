namespace Flit.Admin.Application.DocumentRequirementOverrides.ListDocumentRequirementOverrides;

/// <summary>Resultado del listado de overrides de obligatoriedad por OT. Serializado como <c>{ data }</c>.</summary>
public sealed class ListDocumentRequirementOverridesResult
{
    public ListDocumentRequirementOverridesResult(IReadOnlyList<DocumentRequirementOverrideResponse> data)
    {
        Data = data;
    }

    public IReadOnlyList<DocumentRequirementOverrideResponse> Data { get; }
}

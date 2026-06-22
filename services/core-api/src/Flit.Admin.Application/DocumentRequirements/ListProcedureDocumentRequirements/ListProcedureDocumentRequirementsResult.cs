namespace Flit.Admin.Application.DocumentRequirements.ListProcedureDocumentRequirements;

/// <summary>
/// Resultado del listado. Serializado como <c>{ data }</c> con los documentos
/// asociados ordenados por orden default ascendente (HU #10195, AC2).
/// </summary>
public sealed class ListProcedureDocumentRequirementsResult
{
    public ListProcedureDocumentRequirementsResult(
        IReadOnlyList<ProcedureDocumentRequirementResponse> data)
    {
        Data = data;
    }

    public IReadOnlyList<ProcedureDocumentRequirementResponse> Data { get; }
}

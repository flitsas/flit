namespace Flit.Tramites.Application.BulkTramites.SubmitBatch;

public sealed record SubmitBulkTramitesBatchCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    BulkTramitesTemplateType TemplateType,
    string SourceFilename,
    Stream Content);

public enum SubmitBulkTramitesBatchOutcome
{
    Accepted,
    Rejected,
}

public sealed record SubmitBulkTramitesBatchResult(
    SubmitBulkTramitesBatchOutcome Outcome,
    Guid? BatchId = null,
    int TotalRows = 0,
    int RowsWithStructuralErrors = 0,
    string? Error = null)
{
    public static SubmitBulkTramitesBatchResult Rejected(string error) => new(
        SubmitBulkTramitesBatchOutcome.Rejected, Error: error);
}

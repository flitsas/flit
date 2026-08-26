namespace Flit.Admin.Application.DocumentRequirements.PreviewInformativos;

public enum PreviewDocumentosInformativosOutcome
{
    Resolved,
    ModalidadInvalida,
    ProcedureTypeNotFound,
}

public sealed class PreviewDocumentosInformativosResult
{
    public PreviewDocumentosInformativosOutcome Outcome { get; private init; }
    public IReadOnlyList<DocumentoInformativoItem> Items { get; private init; } = [];
    public string? TipologiaCodigo { get; private init; }
    public Guid? ProcedureTypeId { get; private init; }

    public static PreviewDocumentosInformativosResult ModalidadInvalida() =>
        new() { Outcome = PreviewDocumentosInformativosOutcome.ModalidadInvalida };

    public static PreviewDocumentosInformativosResult ProcedureTypeNotFound() =>
        new() { Outcome = PreviewDocumentosInformativosOutcome.ProcedureTypeNotFound };

    public static PreviewDocumentosInformativosResult Resolved(
        string tipologiaCodigo,
        Guid procedureTypeId,
        IReadOnlyList<DocumentoInformativoItem> items) =>
        new()
        {
            Outcome = PreviewDocumentosInformativosOutcome.Resolved,
            TipologiaCodigo = tipologiaCodigo,
            ProcedureTypeId = procedureTypeId,
            Items = items,
        };
}

namespace Flit.Admin.Application.Plataforma.Mandatos;

/// <summary>Vista efectiva de config de mandato por OT (fila o default implícito generico).</summary>
public sealed record MandateOtConfigView(
    Guid OfficeId,
    string Code,
    string Name,
    string TemplateCode,
    bool RequiresForNaturalPerson,
    string MandataryFamily,
    string? InstitutionalMandataryName,
    string? InstitutionalMandataryNit,
    string? ChamberCity,
    string? MandatarySigla,
    bool HasExplicitConfig,
    long? RowVersion);

public sealed record UpsertMandateOtConfigRequest(
    string TemplateCode,
    bool RequiresForNaturalPerson,
    string MandataryFamily,
    string? InstitutionalMandataryName,
    string? InstitutionalMandataryNit,
    string? ChamberCity,
    string? MandatarySigla,
    long? RowVersion);

public sealed record MandateConfigExtractResult(
    string SuggestedTemplateCode,
    bool RequiresForNaturalPerson,
    string MandataryFamily,
    string? InstitutionalMandataryName,
    string? InstitutionalMandataryNit,
    string? ChamberCity,
    string? MandatarySigla,
    string? Notes);

public enum MandateConfigWriteStatus
{
    Ok,
    OfficeNotFound,
    InvalidTemplate,
    InvalidFamily,
    InstitutionalRequired,
    Conflict,
}

public interface IMandateConfigAdminService
{
    Task<IReadOnlyList<MandateOtConfigView>> ListAsync(CancellationToken ct = default);

    Task<MandateOtConfigView?> GetAsync(Guid officeId, CancellationToken ct = default);

    Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> UpsertAsync(
        Guid officeId,
        UpsertMandateOtConfigRequest request,
        Guid? userId,
        CancellationToken ct = default);

    Task<MandateConfigWriteStatus> DeleteAsync(Guid officeId, CancellationToken ct = default);

    Task<MandateConfigExtractResult> ExtractAsync(
        ReadOnlyMemory<byte> content,
        string mediaType,
        CancellationToken ct = default);
}

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
    long? RowVersion,
    string AssignmentMode = "signer",
    string CustomTemplateKind = "none",
    string? CustomTemplateFileName = null,
    string? CustomTemplateBody = null,
    bool HasCustomTemplate = false,
    /// <summary>
    /// Redacción ELEGIDA para el OT, tal cual está guardada (<c>auto</c> cuando no fija ninguna). Es
    /// distinta de <see cref="TemplateCode"/>, que ya es la EFECTIVA: con <c>auto</c> esa trae la
    /// plantilla de sistema del organismo. El selector de la pantalla necesita la elegida — si se
    /// preseleccionara con la efectiva, abrir y guardar sin tocar nada convertiría un "automática" en
    /// una redacción fija, y el organismo dejaría de seguir a su plantilla de sistema en silencio.
    ///
    /// <para>Literal en vez de <c>MandatoTemplateResolver.Auto</c>: este proyecto no referencia el
    /// dominio de Trámites, donde vive la constante.</para>
    /// </summary>
    string ConfiguredTemplateCode = "auto",
    Guid? DefaultMandateSignerId = null);

public sealed record UpsertMandateOtConfigRequest(
    string TemplateCode,
    bool RequiresForNaturalPerson,
    string MandataryFamily,
    string? InstitutionalMandataryName,
    string? InstitutionalMandataryNit,
    string? ChamberCity,
    string? MandatarySigla,
    long? RowVersion,
    string AssignmentMode = "signer",
    Guid? DefaultMandateSignerId = null);

public sealed record SaveMandateEditorBodyRequest(
    string Body,
    long? RowVersion);

public sealed record MandateConfigExtractResult(
    string SuggestedTemplateCode,
    bool RequiresForNaturalPerson,
    string MandataryFamily,
    string? InstitutionalMandataryName,
    string? InstitutionalMandataryNit,
    string? ChamberCity,
    string? MandatarySigla,
    string? Notes,
    string AssignmentMode = "signer");

public enum MandateConfigWriteStatus
{
    Ok,
    OfficeNotFound,
    InvalidTemplate,
    InvalidFamily,
    InvalidAssignmentMode,
    InstitutionalRequired,
    Conflict,
    InvalidTemplateFile,
    InvalidEditorBody,
    CompanyNotFound,
    InvalidDefaultSigner,
}

public sealed record CompanyOtMandateRuleView(
    Guid CompanyTenantId,
    string CompanyName,
    string AssignmentMode,
    string MandataryFamily,
    string? InstitutionalMandataryName,
    string? InstitutionalMandataryNit,
    string? ChamberCity,
    string? MandatarySigla,
    bool HasExplicitRule,
    Guid? DefaultMandateSignerId = null);

public sealed record UpsertCompanyOtMandateRuleRequest(
    string AssignmentMode,
    string MandataryFamily = "individuo",
    string? InstitutionalMandataryName = null,
    string? InstitutionalMandataryNit = null,
    string? ChamberCity = null,
    string? MandatarySigla = null,
    Guid? DefaultMandateSignerId = null);

public interface IMandateConfigAdminService
{
    /// <summary>
    /// Configuración por OT activos en FLIT (tenant OT dado de alta y <c>is_active</c>).
    /// No lista el catálogo RUNT completo.
    /// </summary>
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

    Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> UploadPdfTemplateAsync(
        Guid officeId,
        Stream content,
        string fileName,
        Guid? userId,
        CancellationToken ct = default);

    Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> SaveEditorBodyAsync(
        Guid officeId,
        SaveMandateEditorBodyRequest request,
        Guid? userId,
        CancellationToken ct = default);

    Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> DeleteCustomTemplateAsync(
        Guid officeId,
        Guid? userId,
        CancellationToken ct = default);

    /// <summary>Bytes del PDF propio (si kind=pdf); null si no aplica.</summary>
    Task<byte[]?> OpenCustomPdfAsync(Guid officeId, CancellationToken ct = default);

    Task<IReadOnlyList<CompanyOtMandateRuleView>> ListCompanyRulesAsync(
        Guid officeId,
        CancellationToken ct = default);

    Task<(MandateConfigWriteStatus Status, CompanyOtMandateRuleView? View)> UpsertCompanyRuleAsync(
        Guid officeId,
        Guid companyTenantId,
        UpsertCompanyOtMandateRuleRequest request,
        Guid? userId,
        CancellationToken ct = default);

    Task<MandateConfigWriteStatus> DeleteCompanyRuleAsync(
        Guid officeId,
        Guid companyTenantId,
        CancellationToken ct = default);
}

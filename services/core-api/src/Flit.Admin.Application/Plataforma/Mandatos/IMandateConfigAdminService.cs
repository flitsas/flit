using Flit.Admin.Domain.Companies.TransitOffices;

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
    Guid? DefaultMandateSignerId = null,
    string? DefaultMandateSignerName = null,
    string? DefaultMandateSignerDocumentType = null,
    string? DefaultMandateSignerDocumentNumber = null,
    string? DefaultMandateSignerIntegrityHash = null);

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

/// <summary>Solo el firmante por defecto del OT. No toca plantilla ni modo de negocio.</summary>
public sealed record SetOtDefaultSignerRequest(
    Guid? DefaultMandateSignerId,
    long? RowVersion = null);

/// <summary>
/// Solo el firmante por defecto de la llave cliente×OT. No toca plantilla del OT.
/// HU #13148 — <paramref name="RowVersion"/> es opcional aquí (el hub del OT no lo envía): si llega y no coincide
/// con el vigente, el servicio responde <see cref="MandateConfigWriteStatus.Conflict"/>.
/// </summary>
public sealed record SetCompanyDefaultSignerRequest(Guid? DefaultMandateSignerId, long? RowVersion = null);

public sealed record SaveMandateEditorBodyRequest(
    string Body,
    long? RowVersion);

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
    Guid? DefaultMandateSignerId = null,
    string? CompanyTaxId = null,
    string? CompanyCode = null,
    string? DefaultMandateSignerName = null,
    string? DefaultMandateSignerDocumentType = null,
    string? DefaultMandateSignerDocumentNumber = null,
    string? DefaultMandateSignerIntegrityHash = null,
    /// <summary>HU #13148 — versión vigente de la regla propia; <c>null</c> cuando la compañía hereda (sin regla).</summary>
    long? RowVersion = null);

/// <summary>
/// Alta o cambio del tipo de mandato de una compañía en un organismo (Super Admin). HU #13148 —
/// <paramref name="RowVersion"/>: sin regla propia se omite (alta); con regla propia es obligatorio y debe ser el
/// vigente, o el servicio responde <see cref="MandateConfigWriteStatus.Conflict"/> y no cambia nada.
/// </summary>
public sealed record UpsertCompanyOtMandateRuleRequest(
    string AssignmentMode,
    string MandataryFamily = "individuo",
    string? InstitutionalMandataryName = null,
    string? InstitutionalMandataryNit = null,
    string? ChamberCity = null,
    string? MandatarySigla = null,
    Guid? DefaultMandateSignerId = null,
    long? RowVersion = null);

/// <summary>
/// HU #13149 — recogida ATÓMICA del tipo de mandato antes y después de escribir la regla compañía×OT, para la
/// bitácora. El servicio la llena con el estado que leyó al escribir (no con una lectura previa aparte), así que
/// el «tipo anterior» es el que de verdad se reemplazó. Solo guarda tipos (<c>signer</c> | <c>institutional</c> |
/// <c>open</c>): ningún dato de personas ni de la entidad.
/// </summary>
public sealed class MandateRuleTypeChange
{
    /// <summary>Tipo vigente antes de escribir (el propio de la regla o el heredado del OT si no había regla).</summary>
    public string? PreviousMode { get; set; }

    /// <summary>Tipo vigente después de escribir (el enviado, o el heredado del OT tras restablecer).</summary>
    public string? NewMode { get; set; }

    /// <summary>La compañía ya tenía regla propia antes de la escritura.</summary>
    public bool HadExplicitRule { get; set; }

    /// <summary>La escritura se confirmó en base de datos.</summary>
    public bool Applied { get; set; }

    /// <summary>Hubo cambio real de tipo (no cuenta repetir el tipo vigente).</summary>
    public bool TypeChanged =>
        Applied && !string.Equals(PreviousMode, NewMode, StringComparison.Ordinal);
}

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

    /// <summary>
    /// Fija o limpia el mandatario general del OT. No cambia <c>template_code</c> ni el resto de la plantilla.
    /// </summary>
    Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> SetOtDefaultSignerAsync(
        Guid officeId,
        SetOtDefaultSignerRequest request,
        Guid? userId,
        CancellationToken ct = default);

    Task<MandateConfigWriteStatus> DeleteAsync(Guid officeId, CancellationToken ct = default);

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

    /// <summary>
    /// Compañías del OT con su regla. Bug #12912 — <paramref name="visibility"/>: el hub del organismo
    /// (no SuperAdmin) solo ve la red que ya le entregó trámites (Ley 1581).
    /// </summary>
    Task<IReadOnlyList<CompanyOtMandateRuleView>> ListCompanyRulesAsync(
        Guid officeId,
        OtCompanyVisibility visibility,
        CancellationToken ct = default);

    Task<(MandateConfigWriteStatus Status, CompanyOtMandateRuleView? View)> UpsertCompanyRuleAsync(
        Guid officeId,
        Guid companyTenantId,
        UpsertCompanyOtMandateRuleRequest request,
        Guid? userId,
        MandateRuleTypeChange? change = null,
        CancellationToken ct = default);

    /// <summary>
    /// Fija o limpia el mandatario de la llave cliente×OT. No reescribe modo ni familia de la regla
    /// existente; una regla nueva hereda el modo del OT.
    /// </summary>
    Task<(MandateConfigWriteStatus Status, CompanyOtMandateRuleView? View)> SetCompanyDefaultSignerAsync(
        Guid officeId,
        Guid companyTenantId,
        SetCompanyDefaultSignerRequest request,
        Guid? userId,
        OtCompanyVisibility visibility,
        CancellationToken ct = default);

    /// <summary>
    /// Bug #12912 — con <see cref="OtCompanyVisibility.DirectOrWithReceivedProcedures"/>, una compañía
    /// que el organismo no puede ver devuelve <see cref="MandateConfigWriteStatus.CompanyNotFound"/>.
    /// </summary>
    /// <remarks>
    /// HU #13148 — <paramref name="expectedRowVersion"/> opcional: si llega y la regla ya cambió, devuelve
    /// <see cref="MandateConfigWriteStatus.Conflict"/> y no borra. Sin regla propia es idempotente (<c>Ok</c>).
    /// </remarks>
    Task<MandateConfigWriteStatus> DeleteCompanyRuleAsync(
        Guid officeId,
        Guid companyTenantId,
        OtCompanyVisibility visibility,
        long? expectedRowVersion = null,
        MandateRuleTypeChange? change = null,
        CancellationToken ct = default);
}

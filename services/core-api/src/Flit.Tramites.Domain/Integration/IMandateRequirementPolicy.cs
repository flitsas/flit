namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Configuración de mandato resuelta para el flujo de trámite: plantilla/custom del OT +
/// <see cref="AssignmentMode"/> de la regla compañía×OT, o del OT si no hay regla.
/// </summary>
public sealed record MandateOtConfig(
    Guid TransitOfficeId,
    string TemplateCode,
    bool RequiresForNaturalPerson,
    string? InstitutionalMandataryName,
    string? InstitutionalMandataryNit,
    string? MandataryFamily = null,
    string? ChamberCity = null,
    string? MandatarySigla = null,
    /// <summary>signer | institutional | open. Sin regla de compañía usa el modo del OT.</summary>
    string? AssignmentMode = null,
    /// <summary>none | pdf | editor.</summary>
    string? CustomTemplateKind = null,
    string? CustomTemplateBody = null,
    string? CustomTemplateStoragePath = null,
    string? CustomTemplateFileName = null,
    /// <summary>Mandatario global del OT. Aplica si no hay default cliente×OT.</summary>
    Guid? OtDefaultMandateSignerId = null,
    /// <summary>Mandatario persona preferido (regla compañía×OT, solo signer).</summary>
    Guid? DefaultMandateSignerId = null);

/// <summary>
/// Puerto para resolver la configuración de mandato del OT del trámite. La plantilla se llavea por
/// código de OT; el tipo (assignment_mode) por compañía gestora × OT cuando se aporta
/// <paramref name="companyTenantId"/>.
/// </summary>
public interface IMandateRequirementPolicy
{
    /// <summary>
    /// Configuración efectiva. Sin fila de OT ⇒ null (default genérico + solo PJ en consumidores legacy).
    /// Sin regla compañía×OT ⇒ modo del OT; sin fila de OT (legado) ⇒ <c>signer</c>.
    /// </summary>
    Task<MandateOtConfig?> ResolveAsync(
        string transitOfficeCode,
        Guid? companyTenantId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Igual que <see cref="ResolveAsync"/> pero llaveando por el <b>id</b> del organismo.
    ///
    /// <para>El código de OT que viaja en <c>field_values.transit_office_code</c> no es confiable como
    /// llave: en la misma tabla conviven códigos RUNT de 7 dígitos (<c>25286000</c>) con códigos DIVIPOLA
    /// de 5 (<c>11001</c>, <c>05266</c>), y el cotejo es por igualdad exacta. Cuando el código guardado no
    /// coincide con el del catálogo NO se encuentra ni la fila de configuración ni la plantilla de
    /// sistema, y el trámite emite el mandato GENÉRICO y sin mandatario aunque el OT esté bien
    /// parametrizado. El id no tiene ese problema.</para>
    /// </summary>
    Task<MandateOtConfig?> ResolveByOfficeIdAsync(
        Guid transitOfficeId,
        Guid? companyTenantId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Política inerte para tests (siempre null).</summary>
public sealed class NullMandateRequirementPolicy : IMandateRequirementPolicy
{
    public static NullMandateRequirementPolicy Instance { get; } = new();

    public Task<MandateOtConfig?> ResolveAsync(
        string transitOfficeCode,
        Guid? companyTenantId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<MandateOtConfig?>(null);

    public Task<MandateOtConfig?> ResolveByOfficeIdAsync(
        Guid transitOfficeId,
        Guid? companyTenantId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<MandateOtConfig?>(null);
}

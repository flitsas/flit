namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Configuración de mandato resuelta para el flujo de trámite: plantilla/custom del OT +
/// <see cref="AssignmentMode"/> de la regla compañía×OT (default <c>signer</c>).
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
    /// <summary>signer | institutional | open. Ausente ⇒ signer.</summary>
    string? AssignmentMode = null,
    /// <summary>none | pdf | editor.</summary>
    string? CustomTemplateKind = null,
    string? CustomTemplateBody = null,
    string? CustomTemplateStoragePath = null,
    string? CustomTemplateFileName = null);

/// <summary>
/// Puerto para resolver la configuración de mandato del OT del trámite. La plantilla se llavea por
/// código de OT; el tipo (assignment_mode) por compañía gestora × OT cuando se aporta
/// <paramref name="companyTenantId"/>.
/// </summary>
public interface IMandateRequirementPolicy
{
    /// <summary>
    /// Configuración efectiva. Sin fila de OT ⇒ null (default genérico + solo PJ en consumidores legacy).
    /// Sin regla compañía×OT ⇒ <c>AssignmentMode = signer</c>.
    /// </summary>
    Task<MandateOtConfig?> ResolveAsync(
        string transitOfficeCode,
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
}

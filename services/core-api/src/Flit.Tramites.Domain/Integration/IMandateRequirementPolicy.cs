namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Configuración de mandato de un Organismo de Tránsito resuelta para el flujo de trámite (ADR-0036,
/// HU #10912). <see cref="RequiresForNaturalPerson"/> activa la exigencia de mandato también a persona
/// natural (Sabaneta). <see cref="TemplateCode"/> selecciona la variante del generador
/// (<c>generico</c>/<c>sabaneta</c>/<c>bello</c>). El mandatario institucional (nombre + NIT) solo lo
/// traen los OT con firmante fijo.
/// </summary>
public sealed record MandateOtConfig(
    Guid TransitOfficeId,
    string TemplateCode,
    bool RequiresForNaturalPerson,
    string? InstitutionalMandataryName,
    string? InstitutionalMandataryNit,
    // HU #11204 — familia del mandatario (individuo | organismo_transito) y datos propios del OT que
    // hasta ahora estaban incrustados en el generador. Un organismo nuevo con formato equivalente a uno
    // ya soportado se da de alta con estos campos, sin desplegar código.
    string? MandataryFamily = null,
    string? ChamberCity = null,
    string? MandatarySigla = null);

/// <summary>
/// Puerto para resolver la configuración de mandato del OT del trámite (ADR-0036, HU #10912). Se llavea
/// por el <b>código</b> del organismo de tránsito (el dato que el trámite porta de forma fiable en
/// <c>field_values.transit_office_code</c>). Desacopla el módulo de trámites del de Admin (mismo patrón
/// que <see cref="ISignatureVaultPolicy"/> / <see cref="IRnmcRequirementPolicy"/>). Devuelve <c>null</c>
/// cuando el OT no tiene configuración: el consumidor aplica el <b>default</b> (plantilla genérica,
/// mandato solo para persona jurídica).
/// </summary>
public interface IMandateRequirementPolicy
{
    /// <summary>
    /// Configuración de mandato del OT con código <paramref name="transitOfficeCode"/>, o <c>null</c>
    /// si el OT no tiene fila de configuración (⇒ default genérico + solo persona jurídica).
    /// </summary>
    Task<MandateOtConfig?> ResolveAsync(
        string transitOfficeCode,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación segura que NUNCA resuelve configuración (devuelve <c>null</c>) — default para tests
/// de dominio/aplicación que no ejercitan el mandato. Con esta política el mandato aplica como en el
/// comportamiento base (solo persona jurídica, plantilla genérica).
/// </summary>
public sealed class NullMandateRequirementPolicy : IMandateRequirementPolicy
{
    public static NullMandateRequirementPolicy Instance { get; } = new();

    public Task<MandateOtConfig?> ResolveAsync(
        string transitOfficeCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<MandateOtConfig?>(null);
}

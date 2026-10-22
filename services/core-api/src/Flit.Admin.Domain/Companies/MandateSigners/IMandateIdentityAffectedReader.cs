namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// HU #13247 (Feature #13245, Épica #13090) — un mandatario Persona natural activo con forma de firma biometría que NO
/// tiene una validación propia aprobada: pierde la firma hasta validarse por el flujo nuevo (decisión 6 del Líder Técnico;
/// sin backfill desde validaciones de trámites). <b>Lleva el correo</b> porque el reporte existe para avisarles: es un dato
/// personal (Ley 1581), por eso solo lo consume el Super Admin y nunca se escribe en logs.
/// </summary>
/// <param name="CompanyTenantId">Compañía vinculada (una fila por vínculo activo); <c>null</c> si no tiene ninguna.</param>
/// <param name="Email">Correo registrado del mandatario; <c>null</c> si no lo tiene (hay que completarlo antes de reenviar).</param>
/// <param name="IdentityStatus"><c>none</c> (sin validación propia: pendiente de validación), <c>pending</c> (en curso) o <c>expired</c>.</param>
public sealed record MandateIdentityAffectedRow(
    Guid MandateSignerId,
    string FullName,
    string? Email,
    Guid? CompanyTenantId,
    string? CompanyName,
    Guid TransitOfficeId,
    string TransitOfficeCode,
    string TransitOfficeName,
    string IdentityStatus);

/// <summary>HU #13247 — puerto del reporte de mandatarios afectados. Solo lectura; solo Super Admin (lo impone el endpoint).</summary>
public interface IMandateIdentityAffectedReader
{
    Task<IReadOnlyList<MandateIdentityAffectedRow>> ListAsync(
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}

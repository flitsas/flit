namespace Flit.Admin.Application.Identity;

/// <summary>
/// Puerto (hexagonal) que VINCULA una validación de identidad administrativa APROBADA a su sujeto, sin
/// que el servicio conozca la tabla concreta del sujeto (HU #10907, ADR-0034). El adaptador despacha por
/// <c>subjectType</c>: para <c>legal_representative</c> setea <c>identity_validation_ref</c> del
/// representante (<c>LegalRepresentative.LinkIdentity</c>). Mantiene el bloque agnóstico del sujeto: un
/// nuevo sujeto (p.ej. mandatario) solo añade una rama al adaptador, sin tocar el servicio.
/// </summary>
public interface IAdminIdentitySubjectLinker
{
    /// <summary>
    /// Vincula la validación <paramref name="validationRef"/> al sujeto (<paramref name="subjectType"/> +
    /// <paramref name="subjectRef"/>) en el tenant. Idempotente. Devuelve <c>true</c> si vinculó (o ya
    /// estaba vinculada), <c>false</c> si el sujeto no existe o el tipo no es soportado.
    /// </summary>
    Task<bool> LinkAsync(
        Guid tenantId,
        string subjectType,
        Guid subjectRef,
        Guid validationRef,
        Guid? actorBy,
        CancellationToken cancellationToken = default);
}

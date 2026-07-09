namespace Flit.Admin.Application.Auditing;

/// <summary>
/// Datos mínimos de una operación de configuración que falló (RNF01, ADR-0024). Grano por
/// operación (no por campo): <see cref="EntityName"/> + <see cref="Operation"/> +
/// <see cref="ErrorCode"/>. No debe transportar valores sensibles: <see cref="ErrorCode"/>
/// es un código estable (p. ej. <c>operation_mode</c>), nunca el valor rechazado.
/// </summary>
/// <param name="TenantId">Tenant al que pertenece la configuración.</param>
/// <param name="EntityName">Entidad lógica afectada (p. ej. <c>transit_office_profile</c>).</param>
/// <param name="Operation">Verbo de la operación (ver <see cref="AuditVocabulary.Operations"/>).</param>
/// <param name="ErrorCode">Código de error estable, sin datos sensibles.</param>
/// <param name="ClientIp">IP de origen ya normalizada, o <c>null</c>.</param>
/// <param name="ChangedBy">Usuario que intentó la operación, si se conoce.</param>
public sealed record AuditFailure(
    Guid TenantId,
    string EntityName,
    string Operation,
    string ErrorCode,
    string? ClientIp,
    Guid? ChangedBy);

/// <summary>
/// Persiste filas de auditoría <c>result = failure</c> en un scope/transacción PROPIO,
/// independiente de la unidad de trabajo del cambio que falló. Así la traza del fallo
/// sobrevive al rollback de la operación principal (decisión clave del ADR-0024, sección 3).
/// </summary>
public interface IAuditFailureWriter
{
    Task WriteAsync(AuditFailure failure, CancellationToken cancellationToken = default);
}

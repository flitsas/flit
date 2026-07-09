namespace Flit.Admin.Application.Auditing;

/// <summary>
/// Fila de auditoría administrativa/seguridad transversal (HU #10678) sobre el rastro
/// unificado <c>admin.tenant_config_audit_logs</c>. Registra CUALQUIER operación sobre
/// usuarios, roles, permisos, autenticación o seguridad — éxito y fallo — con el mínimo
/// exigido: actor, fecha/hora, IP, operación y resultado.
/// </summary>
/// <param name="TenantId">
/// Tenant al que pertenece la operación, o <c>null</c> cuando no es resoluble (p. ej. login
/// fallido con email inexistente).
/// </param>
/// <param name="TenantType">Tipo de tenant denormalizado: <c>COMPANY</c> | <c>TRANSIT_OFFICE</c>, o <c>null</c>.</param>
/// <param name="Module">Categoría transversal (ver <see cref="AuditVocabulary.Modules"/>).</param>
/// <param name="EntityName">Entidad lógica afectada (p. ej. <c>user</c>, <c>invitation</c>, <c>role</c>).</param>
/// <param name="Operation">Verbo de la operación (ver <see cref="AuditVocabulary.Operations"/>).</param>
/// <param name="Result">Desenlace (ver <see cref="AuditVocabulary.Results"/>).</param>
/// <param name="ErrorCode">Código de error estable en filas <c>failure</c>, sin datos sensibles.</param>
/// <param name="ActorUserId">Usuario que ejecutó la operación (<c>changed_by</c>), si se conoce.</param>
/// <param name="TargetEntityType">Tipo de la entidad afectada (p. ej. <c>USER</c>, <c>ROLE</c>).</param>
/// <param name="TargetEntityId">Id de la entidad afectada (el usuario/rol objetivo de la operación).</param>
/// <param name="ClientIp">IP de origen ya normalizada, o <c>null</c>.</param>
/// <param name="UserAgent">User-Agent del cliente, o <c>null</c>.</param>
public sealed record AdminAuditEntry(
    Guid? TenantId,
    string? TenantType,
    string Module,
    string EntityName,
    string Operation,
    string Result,
    string? ErrorCode,
    Guid? ActorUserId,
    string? TargetEntityType,
    Guid? TargetEntityId,
    string? ClientIp,
    string? UserAgent);

/// <summary>
/// Escribe filas en el rastro de auditoría administrativo/seguridad unificado (HU #10678,
/// extensión de RNF01/ADR-0024). Igual que <see cref="IAuditFailureWriter"/>, la
/// implementación usa un scope/transacción PROPIOS para que la escritura sobreviva al
/// rollback de la unidad de trabajo principal y nunca rompa la respuesta al cliente
/// (best-effort).
/// </summary>
public interface IAdminAuditWriter
{
    Task WriteAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default);
}

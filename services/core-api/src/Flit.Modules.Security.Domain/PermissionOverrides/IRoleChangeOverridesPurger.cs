namespace Flit.Modules.Security.Domain.PermissionOverrides;

/// <summary>
/// HU #13646 (ADR-0071, FB-15): al cambiar el rol de un usuario se eliminan (soft delete) sus permisos individuales del
/// tenant y del producto del rol nuevo. Un cambio de rol y su purga son atómicos: si el cambio falla, no se purga.
/// </summary>
public interface IRoleChangeOverridesPurger
{
    /// <summary>Ejecuta <paramref name="work"/> en una sola transacción (se une a la ambiente si ya existe).</summary>
    Task RunAtomicallyAsync(Func<Task> work, CancellationToken ct);

    /// <summary>
    /// Da de baja los overrides activos de (tenant, usuario, producto) y escribe una fila <c>ROLE_CHANGE_PURGE</c> por
    /// permiso, todas con el mismo <c>batch_id</c>. Devuelve cuántos eliminó (0 = no escribe nada).
    /// </summary>
    Task<int> PurgeAsync(Guid tenantId, Guid userId, string productCode, Guid actorUserId, CancellationToken ct);
}

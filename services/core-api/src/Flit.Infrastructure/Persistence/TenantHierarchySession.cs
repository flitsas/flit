using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence;

/// <summary>
/// Fija el actor de la sesión PostgreSQL para el trigger de auditoría del vínculo padre-hija
/// (<c>identity.tr_tenants_hierarchy_audit</c>, HU #12323, ADR-0057). El trigger toma
/// <c>actor_user_id = COALESCE(current_setting('app.current_user_id', true), NEW.updated_by, NEW.created_by)</c>,
/// así que el repositorio que vincule o desvincule un tenant (HU #12355) debe llamar a
/// <see cref="SetCurrentUserAsync"/> <b>dentro de su transacción</b> y antes del <c>SaveChanges</c>
/// que cambia <c>parent_tenant_id</c>. Mismo patrón que <c>set_config('app.current_tenant_id', …, true)</c>
/// en <c>TransitGrantRepository</c>: GUC local a la transacción (<c>is_local = true</c>),
/// parametrizado (sin concatenar SQL) y sin efecto fuera de ella.
/// <para>
/// Uso de ejemplo:
/// <code>
/// await using var tx = await db.Database.BeginTransactionAsync(ct);
/// await TenantHierarchySession.SetCurrentUserAsync(db, actorUserId, ct);
/// child.ParentTenantId = parentId; // el trigger escribe LINK con actor_user_id = actorUserId
/// await db.SaveChangesAsync(ct);
/// await tx.CommitAsync(ct);
/// </code>
/// </para>
/// </summary>
public static class TenantHierarchySession
{
    /// <summary>Nombre del GUC que lee el trigger de auditoría del vínculo.</summary>
    public const string CurrentUserSetting = "app.current_user_id";

    /// <summary>
    /// Ejecuta <c>SELECT set_config('app.current_user_id', @userId, true)</c> en la conexión de
    /// <paramref name="db"/>. En proveedores no relacionales (InMemory, tests) no hace nada: no hay
    /// trigger que alimentar.
    /// </summary>
    public static async Task SetCurrentUserAsync(DbContext db, Guid userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty)
            throw new ArgumentException("El identificador del usuario no puede ser Guid.Empty.", nameof(userId));

        if (!db.Database.IsRelational())
            return;

        await db.Database
            .ExecuteSqlInterpolatedAsync(SetCurrentUserSql(userId), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// SQL parametrizado del GUC (el valor viaja como parámetro; nunca concatenado). Separado para
    /// poder verificar su forma sin un PostgreSQL real.
    /// </summary>
    internal static FormattableString SetCurrentUserSql(Guid userId) =>
        $"SELECT set_config('app.current_user_id', {userId.ToString()}, true)";
}

using System.Linq.Expressions;

namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class UserTempSuspension : Entities.Common.TenantAuditableEntity
{
    public Guid UserId { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    /// <summary>
    /// Fin de la suspensión, o <c>null</c> para desactivación indefinida (HU #10619 AC1):
    /// el usuario queda bloqueado hasta que un administrador lo reactive explícitamente.
    /// </summary>
    public DateTimeOffset? EndsAt { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Regla de vigencia de una suspensión en <paramref name="ahora"/> — la que bloquea el login (HU #10619 AC1/AC5):
    /// no levantada (<c>DeletedAt</c> nulo), ya iniciada y sin fin (desactivación indefinida) o con fin aún no pasado.
    /// Traducible por EF; la reutilizan el login y la revalidación de lotes de consolidados (HU #13375 AC7).
    /// </summary>
    /// <remarks>Uso de ejemplo: <c>db.UserTempSuspensions.Where(UserTempSuspension.VigenteEn(ahora)).AnyAsync(s =&gt; s.UserId == id, ct)</c>.</remarks>
    public static Expression<Func<UserTempSuspension, bool>> VigenteEn(DateTimeOffset ahora) =>
        s => s.DeletedAt == null && s.StartsAt <= ahora && (s.EndsAt == null || s.EndsAt >= ahora);

    public Identity.User User { get; set; } = null!;

    public Identity.Tenant Tenant { get; set; } = null!;
}

namespace Flit.Admin.Domain.Companies.Whitelist;

/// <summary>
/// Repositorio de la lista blanca de correos exentos de la regla de propiedad
/// vehicular (HU #10191, RF05). La implementación (Infrastructure) aplica el
/// contexto RLS de SuperAdmin (<c>SET LOCAL app.current_tenant_id</c>) y persiste
/// de forma atómica el alta junto con su auditoría en una sola transacción.
/// </summary>
public interface IWhitelistRepository
{
    /// <summary>
    /// Indica si <paramref name="email"/> está en la lista blanca del tenant.
    /// La comparación usa la forma normalizada (<see cref="WhitelistEmail.Normalize"/>).
    /// </summary>
    Task<bool> IsEmailWhitelistedAsync(Guid tenantId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Alta masiva atómica de <paramref name="normalizedEmails"/> (ya normalizados):
    /// inserta los nuevos, omite los duplicados y registra una fila de auditoría por
    /// cada correo insertado, todo en una única transacción (todo o nada).
    /// </summary>
    Task<WhitelistAddOutcome> AddEmailsAsync(
        Guid tenantId,
        IReadOnlyList<string> normalizedEmails,
        Guid? addedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Lista los correos activos de la lista blanca del tenant.</summary>
    Task<IReadOnlyList<TenantWhitelistEntry>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

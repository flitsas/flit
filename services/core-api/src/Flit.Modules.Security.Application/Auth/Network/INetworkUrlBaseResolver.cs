namespace Flit.Modules.Security.Application.Auth.Network;

/// <summary>
/// Resuelve la dirección base de un enlace de correo (activación de invitación o recuperación de
/// contraseña) por red MARCA_BLANCA (HU #12423, Feature #12369). La configuración por ambiente
/// (<c>Invitations:ActivateUrlBase</c>, <c>PasswordRecovery:ResetUrlBase</c>, AC6) sigue siendo el
/// respaldo: se usa TAL CUAL cuando no hay dominio de red activo, y aporta la RUTA relativa
/// (<c>PathAndQuery</c>) cuando sí lo hay — nunca se inventa un path nuevo.
/// </summary>
public interface INetworkUrlBaseResolver
{
    /// <summary>
    /// Invitaciones (AC1, AC2, AC4): el destinatario es <paramref name="tenantId"/> (tenant al que
    /// se invita — cabeza o hija de una red). Si <paramref name="tenantId"/> pertenece a una red
    /// MARCA_BLANCA con dominio activo, el enlace usa ese dominio conservando el path de
    /// <paramref name="configuredBase"/>; si no, devuelve <paramref name="configuredBase"/>
    /// LITERAL (misma instancia de string, AC4: igualdad literal).
    /// </summary>
    Task<string> ForTenantAsync(
        Guid tenantId, string configuredBase, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recuperación de contraseña (AC3): el enlace apunta al dominio POR EL QUE SE HIZO la
    /// solicitud (<paramref name="domainContext"/>, sellado por <c>Flit.Gateway</c>) — nunca a uno
    /// deducido de la identidad del usuario. Dominio de red ⇒ ese dominio + path de
    /// <paramref name="configuredBase"/>; dominio FLIT (o sin sello) ⇒ <paramref name="configuredBase"/>
    /// LITERAL.
    /// </summary>
    string ForRequestDomain(IDomainContextAccessor domainContext, string configuredBase);
}

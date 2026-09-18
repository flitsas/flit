namespace Flit.Modules.Security.Application.Auth.Network;

/// <summary>
/// HU #12423 AC5 — coherencia entre el dominio sellado de la petición
/// (<see cref="IDomainContextAccessor"/>) y la red del tenant dueño del token que se está
/// redimiendo (invitación o recuperación de contraseña). Misma regla de pertenencia que
/// <c>ForgotPasswordHandler.ShouldSendForDomainAsync</c> (HU #12422 AC4, ADR-0060 D3): dominio de
/// red R ⇒ solo tenants de R; dominio FLIT ⇒ cualquier tenant EXCEPTO una red MARCA_BLANCA con
/// dominio activo propio (ese tenant debe redimir su token por su propio dominio). No se duplica
/// esa lógica en <c>ForgotPasswordHandler</c> (no se toca, HU #12422 congelada) — este helper es
/// el punto único para los dos consumidores nuevos: <c>ActivateAccountHandler</c> y
/// <c>ResetPasswordHandler</c>.
/// <para>
/// Fail-closed: sin <c>tenantId</c> en dominio de red ⇒ no coherente. El llamador SIEMPRE debe
/// traducir "no coherente" al mismo error genérico de token inválido de hoy — nunca debe revelar
/// a qué red pertenece el token.
/// </para>
/// </summary>
public static class NetworkDomainCoherence
{
    public static async Task<bool> IsCoherentAsync(
        ITenantNetworkMembership networkMembership,
        IDomainContextAccessor domainContext,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(networkMembership);
        ArgumentNullException.ThrowIfNull(domainContext);

        if (domainContext.Kind == DomainKind.Network)
        {
            if (tenantId is not { } networkTenantId)
                return false;

            var membership = await networkMembership.ResolveAsync(networkTenantId, cancellationToken)
                .ConfigureAwait(false);
            return membership.HeadTenantId == domainContext.HeadTenantId;
        }

        if (tenantId is not { } flitTenantId)
            return true;

        var flitMembership = await networkMembership.ResolveAsync(flitTenantId, cancellationToken)
            .ConfigureAwait(false);
        return flitMembership is not { IsMarcaBlancaNetwork: true, ActiveHost: not null };
    }
}

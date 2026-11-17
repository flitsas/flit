namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// Punto de extensión de la activación de cuenta: cada módulo que guarda datos ligados a la INVITACIÓN (porque el usuario
/// aún no existía) los materializa para el usuario recién creado. Se invoca ANTES del <c>SaveChanges</c> de la activación,
/// sobre el mismo contexto: lo que registre el hook se confirma o se revierte junto con el alta del usuario (atómico).
/// </summary>
public interface IUserActivationHook
{
    Task OnUserCreatedAsync(ActivationData data, Guid userId, CancellationToken cancellationToken);
}

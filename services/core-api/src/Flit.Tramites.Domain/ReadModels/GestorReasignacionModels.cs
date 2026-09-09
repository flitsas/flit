namespace Flit.Tramites.Domain.ReadModels;

/// <summary>
/// HU #12162 — candidato a gestor evaluado para la reasignación administrativa de un trámite.
/// Separa las TRES condiciones de disponibilidad (AC2) para que el handler pueda decidir el error
/// exacto sin adivinar por qué se rechazó: <see cref="BelongsToTenant"/> replica EXACTAMENTE el mismo
/// criterio de pertenencia que <c>Flit.Modules.Security.Domain.UserRoles.IUserRoleAssignmentRepository
/// .UserBelongsToTenantAsync</c> (HomeTenantId o asignación de rol activa en el tenant, usuario no
/// eliminado) — no se inventa una validación paralela, se ejecuta la MISMA consulta desde este
/// repositorio (ya expone lecturas de <c>identity.users</c> ad hoc, ver <see cref="GetUserDisplayNamesAsync"/>
/// et al.) para no acoplar <c>Flit.Tramites.Application</c> al módulo Security.
/// </summary>
/// <param name="Id">Id del usuario evaluado (siempre el mismo que se consultó).</param>
/// <param name="DisplayName">Nombre visible del usuario, para el mensaje de error o la fila del selector.</param>
/// <param name="BelongsToTenant">
/// <c>true</c> si el usuario no está eliminado y pertenece al tenant (HomeTenantId o asignación de rol
/// activa) — mismo criterio que <c>UserBelongsToTenantAsync</c>.
/// </param>
/// <param name="IsActive"><c>true</c> si <c>User.Status == "active"</c> (y no eliminado).</param>
/// <param name="IsSuspended">
/// <c>true</c> si tiene una <c>UserTempSuspension</c> vigente en ese tenant AHORA (mismo criterio que
/// la bandera de suspensión que ya expone <c>GET /api/v1/security/users</c>).
/// </param>
public sealed record GestorCandidate(
    Guid Id,
    string DisplayName,
    bool BelongsToTenant,
    bool IsActive,
    bool IsSuspended)
{
    /// <summary>AC2 — "disponible" = pertenece al tenant, está activo y no tiene suspensión vigente.</summary>
    public bool IsAvailable => BelongsToTenant && IsActive && !IsSuspended;
}

/// <summary>
/// HU #12162 (AC-selector) — fila del selector de gestores disponibles para reasignar (consumido por
/// el frontend de HU #12163). Ya filtrada a solo los disponibles (ver
/// <see cref="Repositories.IProcedureInstanceRepository.ListAvailableGestoresAsync"/>): no repite los
/// tres flags de <see cref="GestorCandidate"/> porque en esta proyección siempre son todos verdaderos.
/// </summary>
public sealed record GestorOption(Guid Id, string DisplayName, string Email);

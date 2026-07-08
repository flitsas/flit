using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Modules.Security.Domain.UserRoles;

namespace Flit.Modules.Security.Application.UserManagement.UpdateUser;

/// <summary>
/// Edita nombre y/o correo de un usuario dentro del alcance del caller (HU #10621).
///
/// AC1: si el nombre y/o correo son válidos y distintos, se persisten.
/// AC2: correo ya usado por otra cuenta ACTIVA → <see cref="UserAlreadyExistsException"/> (409
///      <c>USER_ALREADY_EXISTS</c>, mapeado en el endpoint).
/// AC3: correo perteneciente a una cuenta soft-deleted → <see cref="UserEmailBelongsToDeletedAccountException"/>.
/// AC4: <c>RowVersion</c> desactualizado (otro admin ya guardó cambios) → la excepción de
///      concurrencia del repositorio se propaga tal cual; el endpoint la mapea a 409.
/// AC5: no se invalida ninguna sesión activa aquí — es una decisión de diseño explícita: el
///      cambio de correo solo aplica en el próximo login del usuario (el JWT ya emitido sigue
///      siendo válido hasta su expiración natural). No hay nada que hacer en este handler.
/// </summary>
public sealed class UpdateUserHandler(IUserManagementRepository repo)
{
    private const int DisplayNameMaxLength = 150;
    private const int EmailMaxLength = 320;

    public async Task HandleAsync(UpdateUserCommand cmd, CancellationToken ct)
    {
        var target = await repo.FindTargetAsync(cmd.UserId, includeDeleted: false, ct);
        if (target is null)
            throw new TargetUserNotFoundException();

        // Alcance: AdminCompany/OtAdmin solo pueden editar usuarios de SU tenant; SuperAdmin
        // puede editar cualquiera (mismo criterio que AssignRoleHandler/RemoveRoleAssignmentHandler).
        if (!cmd.CallerIsSuperAdmin && target.TenantId != cmd.CallerTenantId)
            throw new UserOutOfScopeException();

        // Trim y "vacío = no tocar el campo" (evita violar el NOT NULL de display_name/email
        // por un submit accidental con el campo en blanco).
        var displayName = cmd.DisplayName?.Trim();
        if (string.IsNullOrEmpty(displayName))
            displayName = null;
        else if (displayName.Length > DisplayNameMaxLength)
            throw new ArgumentException($"El nombre no puede superar {DisplayNameMaxLength} caracteres.", nameof(cmd));

        string? email = null;
        var trimmedEmail = cmd.Email?.Trim();
        if (!string.IsNullOrEmpty(trimmedEmail))
        {
            if (trimmedEmail.Length > EmailMaxLength)
                throw new ArgumentException($"El correo no puede superar {EmailMaxLength} caracteres.", nameof(cmd));

            // Solo se valida unicidad si el correo REALMENTE cambia (case-insensitive) — evita
            // un falso conflicto cuando el admin reenvía el mismo correo actual.
            if (!string.Equals(trimmedEmail, target.Email, StringComparison.OrdinalIgnoreCase))
            {
                var existing = await repo.FindByEmailIncludingDeletedAsync(trimmedEmail, ct);
                if (existing is not null && existing.UserId != cmd.UserId)
                {
                    if (existing.IsDeleted)
                        throw new UserEmailBelongsToDeletedAccountException();
                    throw new UserAlreadyExistsException();
                }

                email = trimmedEmail;
            }
        }

        await repo.UpdateProfileAsync(
            cmd.UserId,
            displayName,
            email,
            cmd.RowVersion,
            DateTimeOffset.UtcNow,
            cmd.CallerId,
            ct);
    }
}

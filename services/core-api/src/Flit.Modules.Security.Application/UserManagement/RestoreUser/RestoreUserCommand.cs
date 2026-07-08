namespace Flit.Modules.Security.Application.UserManagement.RestoreUser;

/// <summary>
/// Restaura (deshace el soft-delete) a un usuario eliminado (HU #10623). SOLO SuperAdmin — la
/// autorización real la aplica el endpoint (<c>SuperAdminPolicy</c>), no este comando.
/// </summary>
public sealed record RestoreUserCommand(Guid UserId, Guid CallerId);

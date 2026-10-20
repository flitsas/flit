namespace Flit.Modules.Security.Application.Auth.ChangePassword;

/// <summary>Cambio voluntario de contraseña por el propio usuario autenticado.</summary>
public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword);

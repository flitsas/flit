namespace Flit.Modules.Security.Application.UserManagement.UpdateUser;

/// <summary>
/// Edición administrativa de nombre y/o correo de un usuario (HU #10621). <c>DisplayName</c> y
/// <c>Email</c> son opcionales: <c>null</c> significa "no tocar ese campo". <c>RowVersion</c> es
/// obligatorio — es la versión que el caller leyó al abrir el formulario (AC4).
/// </summary>
public sealed record UpdateUserCommand(
    Guid CallerTenantId,
    Guid UserId,
    string? DisplayName,
    string? Email,
    long RowVersion,
    Guid CallerId,
    bool CallerIsSuperAdmin);

namespace Flit.Modules.Security.Domain.Auth;

public sealed class IssuedAccessToken
{
    public string Token { get; init; } = string.Empty;

    public int ExpiresInSeconds { get; init; }
}

public interface IJwtTokenIssuer
{
    /// <summary>
    /// Emite el JWT de acceso. HU #10506: <paramref name="roles"/> reemplaza el rol único
    /// anterior — un usuario puede tener varios roles activos simultáneos, cada uno emitido
    /// como su propio claim <c>role</c>/<c>role_id</c>/<c>role_code</c> (ASP.NET Core
    /// <c>RequireRole</c>/<c>IsInRole</c> evalúa contra cualquier claim que matchee).
    /// </summary>
    IssuedAccessToken IssueToken(
        Guid userId,
        string email,
        Guid tenantId,
        string tenantName,
        IReadOnlyList<UserRoleSnapshot> roles,
        IReadOnlyList<string> permissionSlugs);
}

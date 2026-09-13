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
    /// HU #10616: <paramref name="companyNit"/> y <paramref name="entityType"/> se agregan como
    /// claims <c>company_nit</c> y <c>entity_type</c> (además de <c>company_name</c>, que reutiliza
    /// <paramref name="tenantName"/>) para que los consumidores del token identifiquen la empresa/OT
    /// asociada sin llamadas adicionales. <paramref name="companyNit"/> puede venir vacío (AC4).
    /// HU #12345/#12406: <paramref name="tenantType"/> e <paramref name="isGroupParent"/> se emiten
    /// como claims <c>tenant_type</c> e <c>is_group_parent</c> para que el frontend controle la UI
    /// de "Red de clientes" sin round-trips adicionales.
    /// </summary>
    IssuedAccessToken IssueToken(
        Guid userId,
        string email,
        Guid tenantId,
        string tenantName,
        string companyNit,
        string entityType,
        string tenantType,
        bool isGroupParent,
        IReadOnlyList<UserRoleSnapshot> roles,
        IReadOnlyList<string> permissionSlugs);
}

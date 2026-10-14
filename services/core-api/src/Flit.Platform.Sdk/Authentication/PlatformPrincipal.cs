using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Api.Identity;

namespace Flit.Platform.Sdk.Authentication;

/// <summary>
/// Quién llama, leído de un token ya validado (contrato de plataforma v1.3 §3). Un token de servicio tiene como
/// <c>sub</c> el cliente (<c>svc-&lt;código&gt;</c>); uno de usuario, el id del usuario (UUID).
/// </summary>
public static class PlatformPrincipal
{
    public static bool IsServiceSubject(string? subject) =>
        subject is not null
        && subject.StartsWith(OidcDefaults.ServiceClientPrefix, StringComparison.Ordinal)
        && !Guid.TryParse(subject, out _);

    public static bool IsService(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Identity?.IsAuthenticated == true && IsServiceSubject(user.FindFirstValue("sub"));
    }

    /// <summary>SuperAdmin: algún claim <c>role</c> o <c>role_code</c> lo es (multi-rol, sin distinguir mayúsculas).</summary>
    public static bool IsSuperAdmin(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Claims.Any(c => (c.Type == AdminAuthorization.RoleClaimType || c.Type == "role_code")
            && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasScope(ClaimsPrincipal user, string scope)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
    }
}

using System.IdentityModel.Tokens.Jwt;

namespace Flit.Ict.Api.Authorization;

/// <summary>Resultado de evaluar el token de plataforma (del submódulo frontend) para el submódulo ICT.</summary>
public sealed record PlatformAccess(
    bool HasIctLogsAccess,
    bool HasClientAdminAccess,
    bool IsSuperAdmin,
    Guid? TenantId);

/// <summary>
/// Lee el JWT de plataforma reenviado por el Gateway (que ya aplicó su policy JwtRequired) para los
/// submódulos ICT de plataforma (observabilidad + administración de clientes). En el estado transitorio
/// de FLIT 2.0 el token no valida firma en el borde; aquí solo se decodifica para el gate de permiso
/// (ict.logs.read / ict.clients.manage) y el tenant.
/// TODO(ICT-LOG-AUTH): validar firma con la llave pública de plataforma cuando deje de ser transitorio.
/// </summary>
public static class PlatformAccessReader
{
    private const string LogsPermission = "ict.logs.read";
    private const string ClientsManagePermission = "ict.clients.manage";

    public static PlatformAccess Read(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return new PlatformAccess(false, false, false, null);
        }

        var raw = header["Bearer ".Length..].Trim();
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(raw))
        {
            return new PlatformAccess(false, false, false, null);
        }

        var token = handler.ReadJwtToken(raw);

        var isSuperAdmin = token.Claims.Any(c =>
            (c.Type is "role" or "role_code")
            && c.Value.Contains("SUPER", StringComparison.OrdinalIgnoreCase));

        var hasLogs = token.Claims.Any(c => c.Type == "permissions" && c.Value == LogsPermission);
        var hasClientAdmin = token.Claims.Any(c => c.Type == "permissions" && c.Value == ClientsManagePermission);

        Guid? tenantId = Guid.TryParse(token.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value, out var parsed)
            ? parsed
            : null;

        return new PlatformAccess(isSuperAdmin || hasLogs, isSuperAdmin || hasClientAdmin, isSuperAdmin, tenantId);
    }
}

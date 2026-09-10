using System.IdentityModel.Tokens.Jwt;

namespace Flit.Ict.Api.Authorization;

/// <summary>Resultado de evaluar el token de plataforma (del submódulo frontend) para el submódulo ICT.</summary>
/// <param name="HasPiiRevealAccess">
/// Ver los datos personales EN CLARO (HU #11820). Va aparte de <c>HasIctLogsAccess</c> a propósito:
/// si bastara con poder abrir el módulo, el enmascarado no protegería de nada.
/// </param>
/// <param name="Subject">Sujeto del token, para dejar constancia de quién pidió un revelado.</param>
public sealed record PlatformAccess(
    bool HasIctLogsAccess,
    bool HasClientAdminAccess,
    bool IsSuperAdmin,
    Guid? TenantId,
    bool HasPiiRevealAccess = false,
    string Subject = "",
    string Role = "");

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
    private const string PiiRevealPermission = "ict.pii.reveal";

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
        var hasPiiReveal = token.Claims.Any(c => c.Type == "permissions" && c.Value == PiiRevealPermission);
        var subject = token.Claims.FirstOrDefault(c => c.Type is "sub" or "nameid")?.Value ?? string.Empty;
        var role = token.Claims.FirstOrDefault(c => c.Type is "role" or "role_code")?.Value ?? string.Empty;

        Guid? tenantId = Guid.TryParse(token.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value, out var parsed)
            ? parsed
            : null;

        return new PlatformAccess(
            isSuperAdmin || hasLogs,
            isSuperAdmin || hasClientAdmin,
            isSuperAdmin,
            tenantId,
            HasPiiRevealAccess: isSuperAdmin || hasPiiReveal,
            Subject: subject,
            Role: role);
    }
}

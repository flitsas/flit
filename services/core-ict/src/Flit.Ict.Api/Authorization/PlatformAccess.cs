using System.IdentityModel.Tokens.Jwt;

namespace Flit.Ict.Api.Authorization;

/// <summary>Resultado de evaluar el token de plataforma (del submódulo frontend) para el submódulo ICT.</summary>
/// <param name="IsSuperAdmin">
/// Super Administrador FLIT. Bug #13445 (decisión D10): es la ÚNICA llave de Logs ICT
/// (<c>/api/v1/ict/logs</c>, <c>/alerts</c>). El slug <c>ict.logs.read</c> ya no abre nada aquí: la
/// observabilidad del pipeline es de plataforma y no se reparte por rol de empresa.
/// </param>
/// <param name="HasIctTrazabilidadAccess">
/// Abrir la Trazabilidad ICT (Bug #13445, D10): SuperAdmin o el permiso <c>ict.trazabilidad.read</c>.
/// Quien no es SuperAdmin queda atado a su tenant en cada consulta.
/// </param>
/// <param name="HasPiiRevealAccess">
/// Ver los datos personales EN CLARO (HU #11820). Va aparte de <c>HasIctTrazabilidadAccess</c> a
/// propósito: si bastara con poder abrir el módulo, el enmascarado no protegería de nada (D11).
/// </param>
/// <param name="Subject">Sujeto del token, para dejar constancia de quién pidió un revelado.</param>
public sealed record PlatformAccess(
    bool HasClientAdminAccess,
    bool IsSuperAdmin,
    Guid? TenantId,
    bool HasIctTrazabilidadAccess = false,
    bool HasPiiRevealAccess = false,
    string Subject = "",
    string Role = "");

/// <summary>
/// Lee el JWT de plataforma reenviado por el Gateway (que ya aplicó su policy JwtRequired) para los
/// submódulos ICT de plataforma (observabilidad + administración de clientes). En el estado transitorio
/// de FLIT 2.0 el token no valida firma en el borde; aquí solo se decodifica para el gate de permiso
/// (ict.trazabilidad.read / ict.clients.manage / ict.pii.reveal) y el tenant.
/// TODO(ICT-LOG-AUTH): validar firma con la llave pública de plataforma cuando deje de ser transitorio.
/// </summary>
public static class PlatformAccessReader
{
    public const string TrazabilidadPermission = "ict.trazabilidad.read";
    private const string ClientsManagePermission = "ict.clients.manage";
    private const string PiiRevealPermission = "ict.pii.reveal";

    public static PlatformAccess Read(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return new PlatformAccess(false, false, null);
        }

        var raw = header["Bearer ".Length..].Trim();
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(raw))
        {
            return new PlatformAccess(false, false, null);
        }

        var token = handler.ReadJwtToken(raw);

        var isSuperAdmin = token.Claims.Any(c =>
            (c.Type is "role" or "role_code")
            && c.Value.Contains("SUPER", StringComparison.OrdinalIgnoreCase));

        var hasTrazabilidad = token.Claims.Any(c => c.Type == "permissions" && c.Value == TrazabilidadPermission);
        var hasClientAdmin = token.Claims.Any(c => c.Type == "permissions" && c.Value == ClientsManagePermission);
        var hasPiiReveal = token.Claims.Any(c => c.Type == "permissions" && c.Value == PiiRevealPermission);
        var subject = token.Claims.FirstOrDefault(c => c.Type is "sub" or "nameid")?.Value ?? string.Empty;
        var role = token.Claims.FirstOrDefault(c => c.Type is "role" or "role_code")?.Value ?? string.Empty;

        Guid? tenantId = Guid.TryParse(token.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value, out var parsed)
            ? parsed
            : null;

        return new PlatformAccess(
            isSuperAdmin || hasClientAdmin,
            isSuperAdmin,
            tenantId,
            HasIctTrazabilidadAccess: isSuperAdmin || hasTrazabilidad,
            HasPiiRevealAccess: isSuperAdmin || hasPiiReveal,
            Subject: subject,
            Role: role);
    }
}

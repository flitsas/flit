using System.Security.Claims;

namespace Flit.Ict.Api.Authorization;

/// <summary>Resultado de evaluar el token de plataforma (del submódulo frontend) para el submódulo ICT.</summary>
/// <param name="IsSuperAdmin">
/// Super Administrador FLIT. Bug #13445 (decisión D10): es la ÚNICA llave de Logs ICT
/// (<c>/api/v1/ict/logs</c>, <c>/alerts</c>). El slug <c>ict.logs.read</c> ya no abre nada aquí: la
/// observabilidad del pipeline es de plataforma y no se reparte por rol de empresa.
/// </param>
/// <param name="HasIctTrazabilidadAccess">
/// Abrir la Trazabilidad ICT (Bug #13445, D10): SuperAdmin o el permiso <c>ict.trazabilidad.read</c>.
/// Quien no es SuperAdmin queda atado a su tenant en cada consulta; sin <c>tenant_id</c> válido no entra.
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
/// Evalúa el acceso del token de plataforma para los submódulos ICT de plataforma (observabilidad + administración de
/// clientes). Los claims salen SOLO del principal que <see cref="PlatformTokenAuthentication"/> ya validó (firma,
/// emisor, audiencia y vencimiento); un token sin validar nunca concede nada.
/// </summary>
public static class PlatformAccessReader
{
    public const string TrazabilidadPermission = "ict.trazabilidad.read";
    private const string ClientsManagePermission = "ict.clients.manage";
    private const string PiiRevealPermission = "ict.pii.reveal";
    private const string SuperAdminRole = "SuperAdmin";

    public static PlatformAccess Read(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return PlatformTokenAuthentication.GetValidatedPrincipal(context) is { } principal
            ? FromPrincipal(principal)
            : new PlatformAccess(false, false, null);
    }

    internal static PlatformAccess FromPrincipal(ClaimsPrincipal principal)
    {
        var claims = principal.Claims.ToList();

        // Igualdad exacta con el rol (como core-api); nunca Contains: "SUPERVISOR" o "NOTSUPERADMIN" no son SuperAdmin.
        var isSuperAdmin = claims.Any(c =>
            c.Type is "role" or "role_code"
            && string.Equals(c.Value, SuperAdminRole, StringComparison.OrdinalIgnoreCase));

        var hasTrazabilidad = claims.Any(c => c.Type == "permissions" && c.Value == TrazabilidadPermission);
        var hasClientAdmin = claims.Any(c => c.Type == "permissions" && c.Value == ClientsManagePermission);
        var hasPiiReveal = claims.Any(c => c.Type == "permissions" && c.Value == PiiRevealPermission);
        var subject = claims.FirstOrDefault(c => c.Type is "sub" or "nameid")?.Value ?? string.Empty;
        var role = claims.FirstOrDefault(c => c.Type is "role" or "role_code")?.Value ?? string.Empty;

        Guid? tenantId = Guid.TryParse(claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value, out var parsed)
            ? parsed
            : null;

        return new PlatformAccess(
            isSuperAdmin || hasClientAdmin,
            isSuperAdmin,
            tenantId,
            // Fail-closed (Bug #13445 p.4): sin tenant el SQL `@tenant IS NULL OR …` abriría todos los
            // tenants, así que quien no es SuperAdmin necesita un tenant_id válido además del slug.
            HasIctTrazabilidadAccess: isSuperAdmin || (hasTrazabilidad && tenantId is not null),
            HasPiiRevealAccess: isSuperAdmin || (hasPiiReveal && tenantId is not null),
            Subject: subject,
            Role: role);
    }
}

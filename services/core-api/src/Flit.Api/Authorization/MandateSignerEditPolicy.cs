using System.Security.Claims;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #13145 (ADR-0066) — quién puede fijar el mandatario que firma el mandato de un trámite. Lo define el OT
/// (o el Super Admin): el gestor de la compañía solo LO CONSULTA. La restricción aplica únicamente a los
/// usuarios de la compañía gestora; el <c>ot_admin</c>, cualquier usuario de un organismo de tránsito y el
/// SuperAdmin conservan el permiso (si F8 retira el endpoint, esta HU deja el cambio hecho).
/// </summary>
public static class MandateSignerEditPolicy
{
    /// <summary>Código de error (403) cuando un usuario de la compañía intenta cambiar el mandatario.</summary>
    public const string ErrorCode = "mandatario_no_editable_por_gestor";

    public const string ForbiddenMessage =
        "El mandatario que firma el mandato lo define el organismo de tránsito; usted solo puede consultarlo.";

    /// <summary><c>true</c> si el usuario NO es de la compañía gestora (OT, ot_admin o SuperAdmin).</summary>
    public static bool CanSet(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (RequestTenantResolver.IsSuperAdmin(user)
            || RequestTenantResolver.HasRole(user, AdminAuthorization.OtAdminRole))
        {
            return true;
        }

        return user.Claims.Any(c =>
            c.Type == AdminAuthorization.EntityTypeClaimType
            && string.Equals(c.Value, AdminAuthorization.TransitOfficeEntityType, StringComparison.OrdinalIgnoreCase));
    }
}

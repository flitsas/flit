using System.Security.Claims;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Api.Authorization;

/// <summary>
/// Bug #12912 (Ley 1581, decisión del usuario) — traduce el rol de quien llama a la visibilidad de
/// compañías de un organismo de tránsito. SuperAdmin configura la plataforma y ve toda la red; cualquier
/// otro usuario del módulo OT ve las compañías con grant directo y las de la red que ya le entregaron
/// trámites. El corte vive en la capa API porque depende del principal; las capas internas reciben la
/// visibilidad como dato (<see cref="OtCompanyVisibility"/>).
/// </summary>
public static class OtCompanyVisibilityPolicy
{
    public static OtCompanyVisibility For(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsInRole(AdminAuthorization.SuperAdminRole)
            ? OtCompanyVisibility.WholeNetwork
            : OtCompanyVisibility.DirectOrWithReceivedProcedures;
    }
}

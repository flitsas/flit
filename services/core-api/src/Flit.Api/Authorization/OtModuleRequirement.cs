using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Requiere que el caller opere la superficie OT: SuperAdmin, rol <c>ot_admin</c> o cualquier
/// usuario de un tenant organismo de tránsito (<c>entity_type = TRANSIT_OFFICE</c>).
/// Antes la policy era <c>RequireRole(SuperAdmin, ot_admin)</c> y los demás roles de un
/// organismo (p. ej. "Gestor OT") recibían 403 en toda la API del OT.
/// </summary>
public sealed class OtModuleRequirement : IAuthorizationRequirement { }

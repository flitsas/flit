using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Requiere rol SuperAdmin u <c>ot_admin</c> exclusivamente. HU #12859 (Feature #12848,
/// Épica #12751): a diferencia de <see cref="OtModuleRequirement"/>, NO deja pasar por
/// <c>entity_type=TRANSIT_OFFICE</c> — cualquier otro rol de un tenant organismo de tránsito
/// (p. ej. <c>gestor_tramites_ot</c>) recibe 403. Exclusiva de Prelación documental
/// (<c>document-precedence</c>): el Admin OT conserva SOLO el ordenamiento documental.
/// </summary>
public sealed class OtAdminOrSuperAdminRequirement : IAuthorizationRequirement { }

using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Requirement de autorización basado en un slug de permiso del JWT (HU #10165).
/// Se satisface cuando el claim "permissions" del token contiene el slug indicado,
/// o cuando el claim "role_code" es "SuperAdmin" (bypass total, AC4).
/// </summary>
public sealed class PermissionRequirement(string slug) : IAuthorizationRequirement
{
    public string Slug { get; } = slug;
}

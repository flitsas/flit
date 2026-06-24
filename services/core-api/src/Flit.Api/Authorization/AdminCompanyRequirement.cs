using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Requiere que el caller tenga rol AdminCompany o SuperAdmin.
/// Usada en endpoints de gestión de empresa (roles, usuarios del tenant).
/// </summary>
public sealed class AdminCompanyRequirement : IAuthorizationRequirement { }

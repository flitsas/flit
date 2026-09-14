using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #12345 AC4 — exige rol AdminCompany y marca de cabeza de grupo (<c>is_group_parent</c>).
/// </summary>
public sealed class GroupHeadCompanyRequirement : IAuthorizationRequirement;

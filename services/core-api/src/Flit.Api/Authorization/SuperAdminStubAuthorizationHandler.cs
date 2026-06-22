using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Flit.Api.Authorization;

public sealed class SuperAdminStubAuthorizationHandler : AuthorizationHandler<SuperAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SuperAdminRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext &&
            httpContext.Request.Headers.TryGetValue("X-Flit-SuperAdmin", out var value) &&
            value.ToString() == "true")
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

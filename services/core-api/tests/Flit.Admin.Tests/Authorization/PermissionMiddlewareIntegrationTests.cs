using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// Tests del <see cref="SuperAdminForbiddenResultHandler"/> para verificar que el
/// body de la respuesta 403 es correcto según el tipo de requirement que falló
/// (HU #10165, AC3).
///
/// Verifica:
/// - AC3-permission: fallo de <see cref="PermissionRequirement"/> → { code, message }
/// - AC3-superadmin: fallo de otro requirement → { error } (retrocompatibilidad)
/// </summary>
public sealed class ForbiddenResultHandlerBodyTests
{
    private readonly SuperAdminForbiddenResultHandler _handler = new();

    // ── AC3: PermissionRequirement → body { code, message } ─────────────────

    [Fact]
    public async Task AC3_PermissionRequirementFail_Returns403WithCodeAndMessage()
    {
        var (httpContext, responseBody) = BuildHttpContext();
        var requirement = new PermissionRequirement("tramites.read");
        var failure = AuthorizationFailure.Failed([requirement]);
        var authorizeResult = PolicyAuthorizationResult.Forbid(failure);
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(requirement)
            .Build();

        await _handler.HandleAsync(
            _ => Task.CompletedTask,
            httpContext,
            policy,
            authorizeResult);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var json = JsonDocument.Parse(responseBody.ToArray());
        json.RootElement.GetProperty("code").GetString().Should().Be("FORBIDDEN");
        json.RootElement.GetProperty("message").GetString().Should().Be("Permisos insuficientes");
        json.RootElement.TryGetProperty("error", out _).Should().BeFalse(
            "el body de permisos no debe incluir el campo 'error' de SuperAdmin");
    }

    // ── AC3 retrocompatibilidad: otro requirement → body { error } ───────────

    [Fact]
    public async Task AC3_SuperAdminRequirementFail_Returns403WithErrorField()
    {
        var (httpContext, responseBody) = BuildHttpContext();
        var requirement = new SuperAdminRequirement();
        var failure = AuthorizationFailure.Failed([requirement]);
        var authorizeResult = PolicyAuthorizationResult.Forbid(failure);
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(requirement)
            .Build();

        await _handler.HandleAsync(
            _ => Task.CompletedTask,
            httpContext,
            policy,
            authorizeResult);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var json = JsonDocument.Parse(responseBody.ToArray());
        json.RootElement.GetProperty("error").GetString()
            .Should().Be("Acceso restringido: se requiere rol SuperAdmin");
        json.RootElement.TryGetProperty("code", out _).Should().BeFalse(
            "el body de SuperAdmin no debe incluir el campo 'code' de permisos");
    }

    // ── Helper: construir HttpContext con MemoryStream para capturar el body ──

    private static (HttpContext httpContext, MemoryStream responseBody) BuildHttpContext()
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        return (context, body);
    }
}

/// <summary>
/// Genera JWT de prueba con claims de role_code y permissions para los tests de permisos.
/// La firma HS256 es irrelevante — en el entorno de test no hay llave pública configurada.
/// </summary>
internal static class PermissionTestTokenFactory
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    public static string CreateToken(string roleCode, params string[] permissionSlugs)
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("role_code", roleCode),
        };

        foreach (var slug in permissionSlugs)
        {
            claims.Add(new Claim("permissions", slug));
        }

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }
}

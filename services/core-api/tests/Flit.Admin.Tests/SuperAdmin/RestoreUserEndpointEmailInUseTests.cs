using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Flit.Api.Endpoints.SuperAdmin;
using Flit.Modules.Security.Application.UserManagement.RestoreUser;
using Flit.Modules.Security.Domain.UserManagement;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Admin.Tests.SuperAdmin;

/// <summary>
/// Bug #13194 (review 2, B1) — <c>POST /api/v1/superadmin/users/{userId}/restore</c> cuando el correo
/// de la cuenta eliminada ya lo usa otra cuenta VIVA responde <b>409 USER_EMAIL_IN_USE</b> (antes 500 por
/// la violación de <c>uq_users_email</c>). Se monta SOLO el endpoint real
/// (<see cref="SecurityUsersEndpoints"/>) sobre un <see cref="TestServer"/> con el repositorio simulado:
/// el contrato HTTP se verifica sin base de datos.
/// <para>
/// Uso de ejemplo:
/// <code>
/// var response = await client.PostAsync($"/api/v1/superadmin/users/{id}/restore", null, ct);
/// // 409 { code: "USER_EMAIL_IN_USE", message: "Ya existe una cuenta activa con ese correo." }
/// </code>
/// </para>
/// </summary>
public sealed class RestoreUserEndpointEmailInUseTests : IAsyncDisposable
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    private readonly IUserManagementRepository _repo = Substitute.For<IUserManagementRepository>();
    private WebApplication? _app;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_repo);
        builder.Services.AddScoped<RestoreUserHandler>();

        _app = builder.Build();
        // El grupo real exige SuperAdminPolicy; aquí solo importa el contrato del endpoint, así que se
        // inyecta directamente el principal autenticado con su `sub`.
        _app.Use((ctx, next) =>
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", CallerId.ToString())], "test"));
            return next(ctx);
        });
        SecurityUsersEndpoints.Map(_app.MapGroup("/api/v1/superadmin"));
        await _app.StartAsync(Ct);
        return _app.GetTestClient();
    }

    private void UsuarioEliminado() =>
        _repo.FindTargetAsync(UserId, true, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, Guid.NewGuid(), "user@flit.local", "Usuario", DateTimeOffset.UtcNow, 1));

    [Fact]
    public async Task Restore_conCorreoEnUsoPorCuentaViva_Responde409EmailInUse()
    {
        UsuarioEliminado();
        _repo.FindLiveByEmailAsync("user@flit.local", Arg.Any<CancellationToken>())
            .Returns(new ExistingUserByEmail(Guid.NewGuid()));
        var client = await ClientAsync();

        var response = await client.PostAsync($"/api/v1/superadmin/users/{UserId}/restore", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: Ct);
        body!.Code.Should().Be("USER_EMAIL_IN_USE");
        body.Message.Should().Be("Ya existe una cuenta activa con ese correo.");
    }

    /// <summary>La carrera: el repositorio traduce el 23505 de uq_users_email a la misma excepción ⇒ mismo 409.</summary>
    [Fact]
    public async Task Restore_carreraUniqueViolationEnRepositorio_Responde409EmailInUse()
    {
        UsuarioEliminado();
        _repo.RestoreUserAsync(UserId, CallerId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserEmailInUseByLiveAccountException());
        var client = await ClientAsync();

        var response = await client.PostAsync($"/api/v1/superadmin/users/{UserId}/restore", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: Ct);
        body!.Code.Should().Be("USER_EMAIL_IN_USE");
    }

    [Fact]
    public async Task Restore_correoLibre_Responde200()
    {
        UsuarioEliminado();
        var client = await ClientAsync();

        var response = await client.PostAsync($"/api/v1/superadmin/users/{UserId}/restore", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _repo.Received(1).RestoreUserAsync(UserId, CallerId, Arg.Any<CancellationToken>());
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    private sealed record ErrorBody(string Code, string Message);
}

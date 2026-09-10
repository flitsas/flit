using Flit.Modules.Security.Application.UiPreferences.GetUserUiPreference;
using Flit.Modules.Security.Domain.UiPreferences;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UiPreferences;

/// <summary>Tests de <see cref="GetUserUiPreferenceHandler"/> — GET /api/v1/me/ui-preferences/{scope}.</summary>
public sealed class GetUserUiPreferenceHandlerTests
{
    private readonly IUserUiPreferenceRepository _repo = Substitute.For<IUserUiPreferenceRepository>();
    private readonly GetUserUiPreferenceHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private const string Scope = UiPreferenceScopes.TramitesColumns;

    public GetUserUiPreferenceHandlerTests()
    {
        _handler = new GetUserUiPreferenceHandler(_repo);
    }

    // Sin fila guardada, el contrato exige `value: {}`, NUNCA un 404 (lo traduce el handler, no el repo).
    [Fact]
    public async Task HandleAsync_SinFilaGuardada_DevuelveObjetoVacio()
    {
        _repo.FindAsync(TenantId, UserId, Scope, Arg.Any<CancellationToken>())
            .Returns((UserUiPreference?)null);

        var result = await _handler.HandleAsync(
            new GetUserUiPreferenceQuery { TenantId = TenantId, UserId = UserId, Scope = Scope },
            CancellationToken.None);

        result.Scope.Should().Be(Scope);
        result.ValueJson.Should().Be("{}");
    }

    [Fact]
    public async Task HandleAsync_ConFilaGuardada_DevuelveElValorPersistido()
    {
        _repo.FindAsync(TenantId, UserId, Scope, Arg.Any<CancellationToken>())
            .Returns(new UserUiPreference
            {
                TenantId = TenantId,
                UserId = UserId,
                Scope = Scope,
                ValueJson = """{"columns":["placa","estado"]}""",
            });

        var result = await _handler.HandleAsync(
            new GetUserUiPreferenceQuery { TenantId = TenantId, UserId = UserId, Scope = Scope },
            CancellationToken.None);

        result.ValueJson.Should().Be("""{"columns":["placa","estado"]}""");
    }

    // Un scope fuera de la lista blanca se rechaza ANTES de tocar el repositorio.
    [Fact]
    public async Task HandleAsync_ConScopeFueraDeLaListaBlanca_LanzaInvalidUiPreferenceScopeException()
    {
        var act = () => _handler.HandleAsync(
            new GetUserUiPreferenceQuery { TenantId = TenantId, UserId = UserId, Scope = "otro.scope.inventado" },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidUiPreferenceScopeException>();
        await _repo.DidNotReceiveWithAnyArgs().FindAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // Aislamiento: el handler propaga tenantId/userId TAL CUAL al repositorio — nunca los
    // intercambia ni usa un valor por defecto que filtre entre usuarios/tenants.
    [Fact]
    public async Task HandleAsync_PropagaTenantIdYUserIdExactosAlRepositorio()
    {
        var otherTenant = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        _repo.FindAsync(otherTenant, otherUser, Scope, Arg.Any<CancellationToken>())
            .Returns((UserUiPreference?)null);

        await _handler.HandleAsync(
            new GetUserUiPreferenceQuery { TenantId = otherTenant, UserId = otherUser, Scope = Scope },
            CancellationToken.None);

        await _repo.Received(1).FindAsync(otherTenant, otherUser, Scope, Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().FindAsync(TenantId, UserId, Scope, Arg.Any<CancellationToken>());
    }
}

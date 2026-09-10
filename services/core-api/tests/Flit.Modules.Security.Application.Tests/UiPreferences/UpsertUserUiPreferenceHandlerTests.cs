using Flit.Modules.Security.Application.UiPreferences.UpsertUserUiPreference;
using Flit.Modules.Security.Domain.UiPreferences;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UiPreferences;

/// <summary>Tests de <see cref="UpsertUserUiPreferenceHandler"/> — PUT /api/v1/me/ui-preferences/{scope}.</summary>
public sealed class UpsertUserUiPreferenceHandlerTests
{
    private readonly IUserUiPreferenceRepository _repo = Substitute.For<IUserUiPreferenceRepository>();
    private readonly UpsertUserUiPreferenceHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private const string Scope = UiPreferenceScopes.OtProceduresColumns;

    public UpsertUserUiPreferenceHandlerTests()
    {
        _handler = new UpsertUserUiPreferenceHandler(_repo);
    }

    // AC — upsert idempotente: el handler siempre delega en UpsertAsync (crea o actualiza según
    // exista o no la fila); desde Application no hay diferencia entre ambos caminos.
    [Fact]
    public async Task HandleAsync_ConScopeValido_DelegaElUpsertAlRepositorioYDevuelveElResultado()
    {
        const string valueJson = """{"columns":["placa","cliente"]}""";
        _repo.UpsertAsync(TenantId, UserId, Scope, valueJson, Arg.Any<CancellationToken>())
            .Returns(new UserUiPreference
            {
                TenantId = TenantId,
                UserId = UserId,
                Scope = Scope,
                ValueJson = valueJson,
            });

        var result = await _handler.HandleAsync(
            new UpsertUserUiPreferenceCommand { TenantId = TenantId, UserId = UserId, Scope = Scope, ValueJson = valueJson },
            CancellationToken.None);

        result.Scope.Should().Be(Scope);
        result.ValueJson.Should().Be(valueJson);
        await _repo.Received(1).UpsertAsync(TenantId, UserId, Scope, valueJson, Arg.Any<CancellationToken>());
    }

    // Un scope fuera de la lista blanca se rechaza ANTES de tocar el repositorio: nunca persiste
    // un scope inventado, aunque el value venga bien formado.
    [Fact]
    public async Task HandleAsync_ConScopeFueraDeLaListaBlanca_LanzaInvalidUiPreferenceScopeExceptionYNoPersiste()
    {
        var act = () => _handler.HandleAsync(
            new UpsertUserUiPreferenceCommand
            {
                TenantId = TenantId,
                UserId = UserId,
                Scope = "scope.no.permitido",
                ValueJson = "{}",
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidUiPreferenceScopeException>();
        await _repo.DidNotReceiveWithAnyArgs().UpsertAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

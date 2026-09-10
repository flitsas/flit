using Flit.Modules.Security.Domain.UiPreferences;

namespace Flit.Modules.Security.Application.UiPreferences.UpsertUserUiPreference;

/// <summary>
/// Guarda la preferencia de UI del usuario para un scope. Upsert idempotente: la primera llamada
/// crea la fila, las siguientes la sobrescriben — nunca hay que distinguir "crear" de "actualizar"
/// desde el cliente (mismo verbo PUT, mismo shape de respuesta).
/// </summary>
public sealed class UpsertUserUiPreferenceHandler
{
    private readonly IUserUiPreferenceRepository _repository;

    public UpsertUserUiPreferenceHandler(IUserUiPreferenceRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UserUiPreferenceResponse> HandleAsync(
        UpsertUserUiPreferenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!UiPreferenceScopes.IsValid(command.Scope))
        {
            throw new InvalidUiPreferenceScopeException(command.Scope);
        }

        var saved = await _repository
            .UpsertAsync(command.TenantId, command.UserId, command.Scope, command.ValueJson, cancellationToken)
            .ConfigureAwait(false);

        return new UserUiPreferenceResponse(saved.Scope, saved.ValueJson);
    }
}

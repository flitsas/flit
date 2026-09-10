using Flit.Modules.Security.Domain.UiPreferences;

namespace Flit.Modules.Security.Application.UiPreferences.GetUserUiPreference;

/// <summary>
/// Obtiene la preferencia de UI guardada por el usuario para un scope. Contrato explícito con el
/// front: si el usuario nunca la guardó, NO es un 404 — se devuelve <c>value: {}</c>, porque para
/// la UI "sin preferencia guardada" y "preferencia vacía" son el mismo estado (usar las columnas
/// por defecto del catálogo).
/// </summary>
public sealed class GetUserUiPreferenceHandler
{
    private readonly IUserUiPreferenceRepository _repository;

    public GetUserUiPreferenceHandler(IUserUiPreferenceRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UserUiPreferenceResponse> HandleAsync(
        GetUserUiPreferenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!UiPreferenceScopes.IsValid(query.Scope))
        {
            throw new InvalidUiPreferenceScopeException(query.Scope);
        }

        var preference = await _repository
            .FindAsync(query.TenantId, query.UserId, query.Scope, cancellationToken)
            .ConfigureAwait(false);

        return new UserUiPreferenceResponse(query.Scope, preference?.ValueJson ?? "{}");
    }
}

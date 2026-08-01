namespace Flit.Modules.Security.Domain.UiPreferences;

/// <summary>
/// El <c>scope</c> solicitado no está en la lista blanca (<see cref="UiPreferenceScopes"/>). El
/// endpoint la traduce a 400: un scope inventado es un error del cliente, no un caso de negocio
/// a modelar como resultado (a diferencia de "no hay preferencia guardada", que SÍ es normal y
/// se resuelve devolviendo <c>{}</c>, nunca lanzando).
/// </summary>
public sealed class InvalidUiPreferenceScopeException : Exception
{
    public InvalidUiPreferenceScopeException(string? scope)
        : base($"El scope '{scope}' no está permitido para preferencias de UI.")
    {
        Scope = scope;
    }

    public string? Scope { get; }
}

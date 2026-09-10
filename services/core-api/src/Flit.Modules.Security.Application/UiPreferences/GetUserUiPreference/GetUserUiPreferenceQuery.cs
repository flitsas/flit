namespace Flit.Modules.Security.Application.UiPreferences.GetUserUiPreference;

/// <summary>Consulta la preferencia de UI del usuario autenticado para un scope puntual.</summary>
public sealed class GetUserUiPreferenceQuery
{
    public required Guid TenantId { get; init; }

    public required Guid UserId { get; init; }

    public required string Scope { get; init; }
}

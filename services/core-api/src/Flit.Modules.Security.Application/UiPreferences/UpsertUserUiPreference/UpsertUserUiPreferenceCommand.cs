namespace Flit.Modules.Security.Application.UiPreferences.UpsertUserUiPreference;

/// <summary>Guarda (crea o reemplaza) la preferencia de UI del usuario autenticado para un scope.</summary>
public sealed class UpsertUserUiPreferenceCommand
{
    public required Guid TenantId { get; init; }

    public required Guid UserId { get; init; }

    public required string Scope { get; init; }

    /// <summary>JSON crudo del body (<c>value</c>), ya normalizado por el endpoint (nunca null: al menos <c>{}</c>).</summary>
    public required string ValueJson { get; init; }
}

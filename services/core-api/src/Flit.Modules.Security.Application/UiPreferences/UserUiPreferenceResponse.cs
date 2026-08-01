namespace Flit.Modules.Security.Application.UiPreferences;

/// <summary>
/// Shape exacto de respuesta de GET/PUT <c>/api/v1/me/ui-preferences/{scope}</c>. <c>ValueJson</c>
/// viaja como texto JSON crudo hasta el endpoint: es ahí (capa API) donde se deserializa a
/// <c>JsonElement</c> para componer el body de la respuesta, evitando que Application dependa de
/// tipos de serialización concretos.
/// </summary>
public sealed record UserUiPreferenceResponse(string Scope, string ValueJson);

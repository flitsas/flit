namespace Flit.Modules.Security.Application.Auth;

/// <summary>
/// Bug #13194 — normaliza <see cref="SecurityEmailAssetsOptions.BaseUrl"/> para los composers del
/// módulo: vacío o sin options ⇒ <c>null</c> (el layout aplica su respaldo local).
/// </summary>
public static class SecurityEmailAssets
{
    public static string? BaseOrNull(SecurityEmailAssetsOptions? options) =>
        string.IsNullOrWhiteSpace(options?.BaseUrl) ? null : options.BaseUrl.Trim();
}

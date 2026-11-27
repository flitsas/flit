namespace Flit.Admin.Application.Plataforma.DrFlit;

/// <summary>Vista SuperAdmin de la visibilidad UI de DR. FLIT (HU #14771).</summary>
public sealed record DrFlitPlatformSettingsView(
    bool UiEnabled,
    long RowVersion,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy);

/// <summary>Petición PUT del setting de plataforma.</summary>
/// <param name="UiEnabled">Nuevo valor de visibilidad.</param>
/// <param name="RowVersion">Token de concurrencia; <c>null</c> = no verificar (primer guardado).</param>
public sealed record UpdateDrFlitPlatformSettingsRequest(bool UiEnabled, long? RowVersion);

public enum DrFlitPlatformSettingsWriteStatus
{
    Ok,
    Conflict,
}

/// <summary>
/// Lectura/actualización de <c>admin.dr_flit_settings</c> (SuperAdmin) y lectura mínima
/// <c>uiEnabled</c> para usuarios autenticados (Shell).
/// </summary>
public interface IDrFlitPlatformSettingsAdminService
{
    Task<DrFlitPlatformSettingsView> GetAsync(CancellationToken ct = default);

    Task<bool> GetUiEnabledAsync(CancellationToken ct = default);

    Task<(DrFlitPlatformSettingsWriteStatus Status, DrFlitPlatformSettingsView? View)> UpdateAsync(
        UpdateDrFlitPlatformSettingsRequest request,
        Guid? userId,
        CancellationToken ct = default);
}

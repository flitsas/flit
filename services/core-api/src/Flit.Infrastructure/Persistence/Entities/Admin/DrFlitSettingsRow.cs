namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Fila de <c>admin.dr_flit_settings</c> — visibilidad de la UI de DR. FLIT (HU #14771, Feature #14769).
/// Global de plataforma: sin <c>TenantId</c>.
/// </summary>
internal sealed class DrFlitSettingsRow
{
    public Guid Id { get; set; }

    /// <summary><c>true</c> = montar FAB/panel; <c>false</c> = ocultos.</summary>
    public bool UiEnabled { get; set; }

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

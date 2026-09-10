namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Preferencia de UI de un usuario — <c>admin.user_ui_preferences</c>. Base compartida de los
/// criterios de "elegir columnas visibles" en las tablas de trámites: un usuario, un scope, un
/// valor jsonb opaco para el backend.
/// </summary>
public sealed class UserUiPreferenceEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Scope { get; set; } = string.Empty;

    public string Value { get; set; } = "{}";

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

namespace Flit.Admin.Domain.OtProfile;

/// <summary>Perfil operativo del organismo de tránsito por tenant (admin.transit_office_profiles).</summary>
public sealed class OtProfile
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public Guid TransitOfficeId { get; init; }

    public string OperationMode { get; init; } = OtOperationModes.Dashboard;

    public bool QuipuxReadOnly { get; init; }

    /// <summary>
    /// Ventana de revocatoria del trámite aprobado, en días hábiles (HU #12568,
    /// admin.transit_office_profiles.revocation_window_business_days). <c>null</c> = sin
    /// configurar = sin límite; no hay ningún default numérico implícito.
    /// </summary>
    public int? RevocationWindowBusinessDays { get; init; }

    public IReadOnlyList<OtFeatureFlag> FeatureFlags { get; init; } = [];
}

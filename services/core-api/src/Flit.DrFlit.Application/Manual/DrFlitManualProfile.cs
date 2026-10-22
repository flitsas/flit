namespace Flit.DrFlit.Application.Manual;

/// <summary>
/// Perfil EFECTIVO con el que DR. FLIT acota el manual del system prompt (HU #13023). Es el espejo
/// backend de <c>DrFlitRole</c> del frontend (<c>dr-flit-context.ts</c>): un usuario multi-rol colapsa
/// a uno solo con la misma precedencia (SuperAdmin → OT → AdminCompany → Gestor).
/// </summary>
public enum DrFlitManualProfile
{
    Gestor,
    AdminCompany,
    OtAdmin,
    SuperAdmin,
}

/// <summary>
/// Qué audiencias del manual ve cada perfil. Espejo EXACTO de <c>AUDIENCES_BY_PROFILE</c> en
/// <c>frontend/lib/manual/audience.ts</c> (HU-F): si cambia allá, cambia aquí. Los literales son los
/// valores de <c>audience</c> que exporta el artefacto (<c>pnpm manual:export</c>).
/// </summary>
public static class DrFlitManualAudiences
{
    public const string Todos = "Todos";
    public const string Gestor = "Gestor";
    public const string AdminCompania = "Admin de Compañía";
    public const string OrganismoTransito = "Organismo de Tránsito";
    public const string SuperAdmin = "Super Admin";

    private static readonly string[] GestorSet = [Todos, Gestor];
    private static readonly string[] AdminCompanySet = [Todos, Gestor, AdminCompania];
    private static readonly string[] OtAdminSet = [Todos, OrganismoTransito];
    private static readonly string[] SuperAdminSet = [Todos, Gestor, OrganismoTransito, AdminCompania, SuperAdmin];

    public static IReadOnlyList<string> For(DrFlitManualProfile profile) => profile switch
    {
        DrFlitManualProfile.Gestor => GestorSet,
        DrFlitManualProfile.AdminCompany => AdminCompanySet,
        DrFlitManualProfile.OtAdmin => OtAdminSet,
        DrFlitManualProfile.SuperAdmin => SuperAdminSet,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
    };

    /// <summary>
    /// Un artículo sin audiencia declarada es visible para todos (misma regla defensiva que
    /// <c>isVisibleFor</c> en el frontend); con audiencia, comparación exacta contra el set del perfil.
    /// </summary>
    public static bool IsVisible(string? articleAudience, DrFlitManualProfile profile) =>
        string.IsNullOrWhiteSpace(articleAudience)
        || For(profile).Contains(articleAudience, StringComparer.Ordinal);
}

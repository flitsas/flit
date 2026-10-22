namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// HU #13810 (Feature #13808, Épica #13516) — forma canónica del correo de un usuario o una invitación: sin
/// espacios alrededor y en minúsculas. Es la misma que exige la base de datos
/// (<c>CHECK (email = lower(btrim(email)))</c>, DDL 149), así que todo correo que llega del cliente pasa por aquí
/// antes de compararse o guardarse, y las búsquedas usan igualdad exacta (sin <c>ILIKE</c>, donde <c>_</c> y
/// <c>%</c> son comodines).
/// <para>
/// Uso de ejemplo: <c>EmailNormalizer.Normalize(" Juan@X.com ")</c> → <c>"juan@x.com"</c>;
/// <c>EmailNormalizer.Normalize(null)</c> → <c>""</c>.
/// </para>
/// </summary>
public static class EmailNormalizer
{
    public static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
}

namespace Flit.Admin.Domain.Companies.Whitelist;

/// <summary>
/// Normalización canónica de los correos de la lista blanca (HU #10191, RF05).
/// El almacenamiento y la comparación usan siempre la forma normalizada
/// (recorte + minúsculas) para que la unicidad por tenant (<c>uq_tenant_whitelist_users_tenant_email</c>)
/// y la exención del interceptor (AC2) sean insensibles a mayúsculas/espacios.
/// </summary>
public static class WhitelistEmail
{
    /// <summary>Devuelve el correo recortado y en minúsculas (cultura invariante).</summary>
    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }
}

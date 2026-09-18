namespace Flit.Gateway.Configuration;

/// <summary>
/// Excepción documentada del sello <c>X-Flit-Domain</c> (HU #12417 AC1/AC5,
/// delta-hechos-post-adr.md hecho 7): el frontend interno (red Docker de <c>docker-compose.prod.yml</c>)
/// llama a <c>BRANDING_INTERNAL_API_URL</c> con el sello explícito porque, cuando el hairpin público
/// no es posible, no hay otra forma de decirle al Gateway qué dominio está resolviendo. Solo se
/// acepta el sello ENTRANTE si la IP remota cae en una de estas redes; para cualquier otro origen
/// (tráfico público) el Gateway SIEMPRE sobrescribe con el host real de la conexión.
/// </summary>
public sealed class DomainSealOptions
{
    public const string SectionName = "DomainSeal";

    /// <summary>
    /// CIDR (p. ej. <c>172.16.0.0/12</c>) de las redes internas de confianza. Vacío por defecto:
    /// fail-closed — nunca se confía en el sello entrante, el Gateway siempre sella con el host real.
    /// </summary>
    public IReadOnlyList<string> InternalAllowedNetworks { get; set; } = [];
}

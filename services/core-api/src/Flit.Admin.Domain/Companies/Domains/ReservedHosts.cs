namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Dominios reservados (HU #12416 AC3): el dominio de FLIT y sus subdominios, y el portal de
/// organismos de tránsito. La BASE no conoce el dominio de despliegue (DDL 116, comentario de tabla) —
/// la lista vive en configuración (<c>Domains:Reserved</c>, <c>DomainOptions.Reserved</c>) y se evalúa
/// aquí, en dominio puro. Patrones admitidos: exacto (<c>flitsas.online</c>) o comodín de un nivel
/// (<c>*.flitsas.online</c>, que también reserva el dominio base sin subdominio).
/// </summary>
public static class ReservedHosts
{
    public static bool IsReserved(string normalizedHost, IEnumerable<string> reservedPatterns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedHost);
        ArgumentNullException.ThrowIfNull(reservedPatterns);

        foreach (var raw in reservedPatterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var pattern = raw.Trim().ToLowerInvariant();

            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                var baseDomain = pattern[2..];
                var suffix = pattern[1..]; // ".flitsas.online"

                if (string.Equals(normalizedHost, baseDomain, StringComparison.Ordinal)
                    || normalizedHost.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (string.Equals(normalizedHost, pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

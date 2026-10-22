namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Dominios reservados (HU #12416 AC3): el dominio de FLIT y sus subdominios, y el portal de
/// organismos de tránsito. La BASE no conoce el dominio de despliegue (DDL 116, comentario de tabla) —
/// la lista vive en configuración (<c>Domains:Reserved</c>, <c>DomainOptions.Reserved</c>) y se evalúa
/// aquí, en dominio puro. Patrones admitidos: exacto (<c>flitsas.online</c>) o comodín
/// (<c>*.flitsas.online</c>, que reserva cualquier subdominio a cualquier profundidad y también el
/// dominio base sin subdominio).
/// <para>
/// Excepción (HU #12761): <paramref name="allowedHosts"/> lista hosts de prueba de marca blanca que
/// viven dentro de la zona reservada (<c>Domains:Allowed</c>, <c>DomainOptions.Allowed</c>). Se
/// evalúa ANTES que los patrones y la coincidencia es EXACTA — sin comodines: un subdominio de un
/// host exceptuado sigue siendo reservado.
/// </para>
/// <para>
/// Comparación: TODAS las comprobaciones (permitidos y reservados, exactos y comodín) usan
/// <see cref="StringComparison.OrdinalIgnoreCase"/>, insensible a mayúsculas en AMBOS lados — host y
/// entrada de configuración. Es deliberado que el bucle de reservados también lo sea: ante un host sin
/// minusculizar el guardarraíl debe fallar CERRADO (clasificarlo como reservado) en vez de dejarlo
/// escapar. Aun así, normalizar el host antes de llamar (ver <see cref="HostNormalizer"/>) sigue
/// siendo responsabilidad del llamador: aquí solo se compara, no se convierte IDN ni se valida formato.
/// </para>
/// </summary>
public static class ReservedHosts
{
    public static bool IsReserved(
        string normalizedHost,
        IEnumerable<string> reservedPatterns,
        IEnumerable<string>? allowedHosts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedHost);
        ArgumentNullException.ThrowIfNull(reservedPatterns);

        if (allowedHosts is not null)
        {
            foreach (var rawAllowed in allowedHosts)
            {
                if (string.IsNullOrWhiteSpace(rawAllowed))
                {
                    continue;
                }

                if (string.Equals(normalizedHost.Trim(), rawAllowed.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

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

                if (string.Equals(normalizedHost, baseDomain, StringComparison.OrdinalIgnoreCase)
                    || normalizedHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(normalizedHost, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

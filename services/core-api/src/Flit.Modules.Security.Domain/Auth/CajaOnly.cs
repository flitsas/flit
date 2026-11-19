namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// Perfil «solo caja» (HU #14454, Épica #14210, ADR-0082): quien tiene <c>ot_caja</c> como único rol activo inicia sesión en FLIT 2.0 con el
/// login normal pero solo puede usar la caja del organismo (core-api lo limita). Un usuario con además cualquier otro rol conserva el acceso que ese rol le da.
/// </summary>
public static class CajaOnly
{
    /// <summary>Código del rol de la caja del organismo (producto <c>caja</c>, <c>TRANSIT_OFFICE</c>).</summary>
    public const string RoleCode = "ot_caja";

    /// <summary>Código estable con el que Identidad niega los tokens OIDC de producto (que no llevan el rol ot_caja) a un usuario solo-caja.</summary>
    public const string DeniedCode = "CAJA_ONLY";

    /// <summary><c>true</c> si hay al menos un rol y TODOS son <c>ot_caja</c>. Sin roles no es solo-caja: el comportamiento de siempre.</summary>
    public static bool IsCajaOnly(IEnumerable<string> roleCodes)
    {
        ArgumentNullException.ThrowIfNull(roleCodes);
        var any = false;
        foreach (var code in roleCodes)
        {
            if (!string.Equals(code, RoleCode, StringComparison.OrdinalIgnoreCase))
                return false;
            any = true;
        }

        return any;
    }
}

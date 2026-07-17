namespace Flit.Modules.Quipux.Domain.Codigos;

/// <summary>
/// Códigos del protocolo Quipux. En FLIT 1.0 estaban como literales sueltos repartidos por el
/// código (<c>=== 81</c>, <c>=== 2</c>, <c>=== 3</c>) y duplicados entre traspasos y matrículas.
/// </summary>
public static class QuipuxCodigos
{
    /// <summary>Documento radicado con éxito. Único código de registro que 1.0 trata como éxito.</summary>
    public const int RegistroOk = 81;

    /// <summary>
    /// Error de registro. En 1.0 no lo devuelve Quipux: lo escribe FLIT en el log cuando el POST
    /// falla o expira, y es el gancho del job de reintento. Aquí se conserva por paridad de datos.
    /// </summary>
    public const int RegistroError = 0;

    /// <summary>Trámite aprobado por la secretaría (<c>estadoTramite.codigo</c>).</summary>
    public const int TramiteAprobado = 2;

    /// <summary>Trámite rechazado por la secretaría (<c>estadoTramite.codigo</c>).</summary>
    public const int TramiteRechazado = 3;

    /// <summary>¿El registro fue aceptado por Quipux?</summary>
    public static bool EsRegistroExitoso(int? codigo) => codigo == RegistroOk;

    /// <summary>¿Quipux ya resolvió el trámite (aprobado o rechazado)?</summary>
    public static bool EsEstadoTerminal(int? estadoTramite) =>
        estadoTramite is TramiteAprobado or TramiteRechazado;
}

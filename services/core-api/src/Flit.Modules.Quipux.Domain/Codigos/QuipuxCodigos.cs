namespace Flit.Modules.Quipux.Domain.Codigos;

/// <summary>
/// Códigos del protocolo Quipux. En FLIT 1.0 estaban como literales sueltos repartidos por el
/// código (<c>=== 81</c>, <c>=== 2</c>, <c>=== 3</c>) y duplicados entre traspasos y matrículas.
/// Catálogo <c>registroDocumento</c> (proveedor): 061–096 / 081–085.
/// </summary>
public static class QuipuxCodigos
{
    /// <summary>Documento radicado con éxito (<c>081</c>).</summary>
    public const int RegistroOk = 81;

    /// <summary>
    /// El documento ya está almacenado en Quipux (<c>082</c>). Idempotente: no re-POST,
    /// marcar submission como registrada.
    /// </summary>
    public const int RegistroYaExiste = 82;

    /// <summary>
    /// Error de registro. En 1.0 no lo devuelve Quipux: lo escribe FLIT en el log cuando el POST
    /// falla o expira, y es el gancho del job de reintento. Aquí se conserva por paridad de datos.
    /// </summary>
    public const int RegistroError = 0;

    /// <summary>Trámite aprobado por la secretaría (<c>estadoTramite.codigo</c>).</summary>
    public const int TramiteAprobado = 2;

    /// <summary>Trámite rechazado por la secretaría (<c>estadoTramite.codigo</c>).</summary>
    public const int TramiteRechazado = 3;

    /// <summary>¿Quipux aceptó la radicación o confirma que el documento ya estaba?</summary>
    public static bool EsRegistroExitoso(int? codigo) =>
        codigo is RegistroOk or RegistroYaExiste;

    /// <summary>¿Respuesta 082 — documento ya registrado (idempotente)?</summary>
    public static bool EsRegistroYaExistente(int? codigo) => codigo == RegistroYaExiste;

    /// <summary>¿Quipux ya resolvió el trámite (aprobado o rechazado)?</summary>
    public static bool EsEstadoTerminal(int? estadoTramite) =>
        estadoTramite is TramiteAprobado or TramiteRechazado;
}

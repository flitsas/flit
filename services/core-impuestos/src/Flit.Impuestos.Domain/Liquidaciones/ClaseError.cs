namespace Flit.Impuestos.Domain.Liquidaciones;

/// <summary>
/// Qué hacer con un ítem en <c>ERROR</c> (columna <c>liquidacion_items.error_clase</c>), según la tabla §9 de la guía del API de
/// liquidación. En la base se guarda en mayúsculas con guion bajo (<c>CORREGIR_DATOS</c>).
/// </summary>
public enum ClaseError
{
    /// <summary>Los datos no sirven: no se reintenta; se corrigen y se crea otra consulta.</summary>
    CorregirDatos,

    /// <summary>Depende del mensaje del portal (o el código no es conocido): decide una persona.</summary>
    Depende,

    /// <summary>Problema temporal: se puede relanzar como consulta nueva.</summary>
    Reintentable,
}

/// <summary>
/// Clase de cada código de error de la consulta (<c>error_code</c> cuando <c>consulta.status</c> es <c>failed</c>, guía del API
/// §9). Los códigos se comparan exactos, en minúsculas como los envía el API. <c>pdf_download_failed</c> y <c>s3_error</c> no
/// son errores de la consulta: acompañan un resultado <c>partial</c>.
/// </summary>
/// <example>
/// <code>
/// ClasificacionErrores.De("rate_limited");          // ClaseError.Reintentable
/// ClasificacionErrores.EsParcial("pdf_download_failed"); // true
/// </code>
/// </example>
public static class ClasificacionErrores
{
    /// <summary>Tabla §9: código del API → clase.</summary>
    public static IReadOnlyDictionary<string, ClaseError> Tabla { get; } = new Dictionary<string, ClaseError>(StringComparer.Ordinal)
    {
        ["invalid_params"] = ClaseError.CorregirDatos,
        ["owner_validation_failed"] = ClaseError.CorregirDatos,
        ["vehicle_data_required"] = ClaseError.CorregirDatos,
        ["portal_rejected"] = ClaseError.Depende,
        ["captcha_blocked"] = ClaseError.Reintentable,
        ["auth_failed"] = ClaseError.Reintentable,
        ["rate_limited"] = ClaseError.Reintentable,
        ["timeout"] = ClaseError.Reintentable,
        ["http_status"] = ClaseError.Reintentable,
        ["proxy_failed"] = ClaseError.Reintentable,
        ["source_inactive"] = ClaseError.Reintentable,
        ["adapter_unavailable"] = ClaseError.Reintentable,
        ["parse_error"] = ClaseError.Reintentable,
        ["unexpected"] = ClaseError.Reintentable,
        ["worker_lost"] = ClaseError.Reintentable,
    };

    /// <summary>Código con que el API rechaza la consulta cuando lo decide el portal (clase <see cref="ClaseError.Depende"/>).</summary>
    public const string CodigoPortalRechazo = "portal_rejected";

    /// <summary>
    /// Fragmento del mensaje del portal cuando la placa no está inscrita, p. ej. «EviewDetailVehicles: La placa ingresada no se
    /// encuentra inscrita en nuestra base de datos.».
    /// </summary>
    public const string FragmentoPlacaNoInscrita = "no se encuentra inscrita";

    private static readonly HashSet<string> Parciales = new(StringComparer.Ordinal) { "pdf_download_failed", "s3_error" };

    /// <summary>Clase del código; un código que la tabla no conoce (o vacío) es <see cref="ClaseError.Depende"/>.</summary>
    public static ClaseError De(string? codigo) =>
        codigo is not null && Tabla.TryGetValue(codigo, out var clase) ? clase : ClaseError.Depende;

    /// <summary>El código acompaña un resultado parcial (los montos están, faltó un PDF).</summary>
    public static bool EsParcial(string? codigo) => codigo is not null && Parciales.Contains(codigo);

    /// <summary>
    /// El portal rechazó la consulta porque la placa no está inscrita: código <see cref="CodigoPortalRechazo"/> (exacto) y un mensaje
    /// que contiene <see cref="FragmentoPlacaNoInscrita"/> sin distinguir mayúsculas. El ítem sigue en <c>ERROR</c> con clase
    /// <see cref="ClaseError.Depende"/>, pero se puede relanzar (decisión del usuario, 2026-11-26).
    /// </summary>
    /// <example>
    /// <code>
    /// ClasificacionErrores.EsPlacaNoInscrita("portal_rejected", "La placa ingresada no se encuentra inscrita en nuestra base de datos."); // true
    /// ClasificacionErrores.EsPlacaNoInscrita("portal_rejected", "Mantenimiento del portal."); // false
    /// </code>
    /// </example>
    public static bool EsPlacaNoInscrita(string? codigo, string? mensaje) =>
        string.Equals(codigo, CodigoPortalRechazo, StringComparison.Ordinal)
        && mensaje is not null
        && mensaje.Contains(FragmentoPlacaNoInscrita, StringComparison.OrdinalIgnoreCase);
}

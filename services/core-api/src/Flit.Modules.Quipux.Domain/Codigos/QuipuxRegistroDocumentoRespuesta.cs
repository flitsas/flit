namespace Flit.Modules.Quipux.Domain.Codigos;

/// <summary>
/// Clasificación operativa del <c>codigo</c> de <c>registroDocumento</c> según catálogo del proveedor
/// (HTTP + mensaje). Decide si marcar éxito, reintentar o dead-letter sin más POST.
/// </summary>
public enum QuipuxRegistroClasificacion
{
    /// <summary><c>081</c> — datos almacenados correctamente.</summary>
    Exitoso,

    /// <summary><c>082</c> — documento ya registrado; no reenviar.</summary>
    YaRegistrado,

    /// <summary>Fallo temporal (red, S3, timeout, OT indisponible); conviene reintentar.</summary>
    Reintentable,

    /// <summary>Error de negocio / validación / permanente; no reintentar.</summary>
    Definitivo,
}

/// <summary>Mapa de códigos de respuesta de <c>registroDocumento</c> (catálogo Quipux).</summary>
public static class QuipuxRegistroDocumentoRespuesta
{
    /// <summary>Clasifica el código para el worker registrador.</summary>
    public static QuipuxRegistroClasificacion Clasificar(int? codigo) => codigo switch
    {
        QuipuxCodigos.RegistroOk => QuipuxRegistroClasificacion.Exitoso,
        QuipuxCodigos.RegistroYaExiste => QuipuxRegistroClasificacion.YaRegistrado,

        // HTTP 200 / 500 — indisponibilidad o infra Quipux/OT/S3
        71 or 72 or 74 or 75 or 76 or 85 => QuipuxRegistroClasificacion.Reintentable,

        // Validación / negocio / auth de payload (no se arregla reenviando el mismo PDF)
        61 or 62 or 63 or 65 or 66 or 67 or 68 or 69 or 70
            or 73 or 77 or 78 or 83 or 84 or 95 or 96
            => QuipuxRegistroClasificacion.Definitivo,

        // Código desconocido: no quemar reintentos en bucle (PDN).
        _ => QuipuxRegistroClasificacion.Definitivo,
    };

    /// <summary>Mensaje canónico del catálogo (para audit / Discussion); null si no está listado.</summary>
    public static string? MensajeCatalogo(int? codigo) => codigo switch
    {
        61 => "Información no válida",
        62 => "Información no válida",
        63 => "La información no pudo ser almacenada",
        65 => "Las credenciales no son válidas",
        66 => "Token vencido",
        67 => "El documento no cumple con el formato permitido",
        68 => "El documento excede el peso permitido",
        69 => "El usuario no tiene los permisos necesarios",
        70 => "El identificador ingresado no cumple con el formato válido",
        71 => "Información no disponible",
        72 => "Se ha excedido el tiempo máximo de espera",
        73 => "Información no disponible",
        74 => "Información no disponible",
        75 => "Información no disponible",
        76 => "Información no disponible",
        77 => "Información no válida",
        78 => "Información no válida",
        QuipuxCodigos.RegistroOk => "Los datos se almacenaron correctamente",
        QuipuxCodigos.RegistroYaExiste => "El documento a registrar ya se encuentra almacenado en el sistema",
        83 => "El vehículo no se encuentra registrado para el organismo de tránsito",
        84 => "El vehículo no se encuentra registrado para el organismo de tránsito",
        85 => "La información no pudo ser almacenada",
        95 => "Información no válida",
        96 => "Información no válida",
        _ => null,
    };
}

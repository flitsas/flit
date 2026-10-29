namespace Flit.PersonaNatural.Api.Cotizaciones;

/// <summary>
/// Configuración del catálogo y la cotización del portal (HU #14043). Sin <see cref="CoreApiAddress"/> el servicio arranca igual
/// y los endpoints responden 503 <c>servicio_no_disponible</c>: el catálogo es opcional hasta que el ambiente lo configure.
/// </summary>
public sealed class CotizacionesOptions
{
    public const string SectionName = "Cotizaciones";

    /// <summary>URL interna de la fachada gRPC de core-api (p. ej. <c>http://core-api:8080</c>). Vacía = sin catálogo.</summary>
    public string? CoreApiAddress { get; set; }

    /// <summary>URL interna de core-notificaciones (p. ej. <c>http://core-notificaciones:8085</c>). Vacía = sin envío de correo.</summary>
    public string? NotificacionesAddress { get; set; }

    /// <summary>Caché corta del catálogo (organismos y tipos): la cotización NUNCA se cachea.</summary>
    public int CacheCatalogoSegundos { get; set; } = 60;

    /// <summary>Envíos de cotización por correo permitidos por ventana (por huella del correo, no por IP).</summary>
    public int LimiteCorreoPorVentana { get; set; } = 3;

    public int VentanaCorreoMinutos { get; set; } = 60;

    /// <summary>Cuánto se recuerda una <c>Idempotency-Key</c> para no reenviar el mismo correo.</summary>
    public int IdempotenciaMinutos { get; set; } = 30;
}

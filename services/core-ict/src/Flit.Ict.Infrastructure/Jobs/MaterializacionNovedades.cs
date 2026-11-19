using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Infrastructure.Jobs;

/// <summary>
/// Bug #13304 (capa 3): advertencias NO fatales que core-api devuelve al materializar el borrador y que
/// el gestor/tercero debe ver. <c>CreateDraftFromIct</c> crea el borrador aunque no pueda guardar los
/// actores (<c>actors_warning:*</c>) o los datos comerciales (<c>commercial_warning:*</c>); antes esos
/// códigos solo se logueaban y el trámite llegaba a FLIT sin actores ni precio de venta sin que nadie en
/// ICT lo supiera.
/// </summary>
/// <remarks>
/// El reply concatena varias advertencias con <c>;</c> (<c>IctOrchestrationService.AppendWarning</c>):
/// <c>"seed_warning:x;actors_warning:invalid_document_type"</c>. Se publican las de actores, comerciales y,
/// desde el numeral 3, las del preflight (<c>preflight_warning:*</c>: consulta RUNT ausente/vencida/de otro
/// vehículo o bloqueo del preflight; sin ellas el borrador nace sin prenda/gravamen y nadie en ICT lo sabe).
/// Las demás (seed/identity/attachments) no dejan el trámite sin datos que ICT haya recibido. Los códigos no
/// llevan PII (son códigos de error y, como mucho, el rol del actor).
/// </remarks>
internal static class MaterializacionNovedades
{
    private static readonly string[] PrefijosVisibles =
        ["actors_warning:", "commercial_warning:", "preflight_warning:", "carroceria_warning:", "color_warning:", "combustible_warning:"];

    /// <summary>
    /// Texto en español de las advertencias visibles que lo ameritan (el resto se publica como código). Se
    /// conserva el código entre paréntesis para trazabilidad. Sin PII.
    /// </summary>
    private static readonly Dictionary<string, string> TextoDeAdvertencia = new(StringComparer.Ordinal)
    {
        ["carroceria_warning:fuera_de_catalogo"] =
            "La carrocería enviada no corresponde al catálogo para la clase del vehículo consultado; el gestor debe elegirla",
        ["color_warning:fuera_de_catalogo"] =
            "El color enviado no corresponde al catálogo de colores; el gestor debe elegirlo",
        ["combustible_warning:sin_equivalencia"] =
            "El combustible enviado no tiene equivalencia en el catálogo; el gestor debe elegirlo",
        ["carroceria_warning:vehiculo_sin_carroceria"] =
            "El vehículo no tiene carrocería registrada en el RUNT; el cambio de carrocería no aplica y no se incluyó en el trámite",
    };

    /// <summary>Prefijo del texto que se agrega a los comentarios del master.</summary>
    internal const string Prefijo = "borrador creado en FLIT con novedades: ";

    /// <summary>Outcome del evento de timeline cuando el borrador nació con advertencias visibles.</summary>
    internal const string OutcomeConNovedades = "con_novedades";

    /// <summary>Códigos visibles (actores/comercial) contenidos en el ErrorCode del reply, sin repetidos.</summary>
    internal static IReadOnlyList<string> Visibles(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return [];
        }

        return errorCode
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => PrefijosVisibles.Any(p => w.StartsWith(p, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Une el ErrorCode del reply con las advertencias locales, sin repetir códigos.</summary>
    internal static string? Combinar(string? errorCode, IReadOnlyList<string> locales)
    {
        ArgumentNullException.ThrowIfNull(locales);
        var presentes = (errorCode ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extra = locales.Where(l => !string.IsNullOrWhiteSpace(l) && !presentes.Contains(l, StringComparer.Ordinal)).ToList();
        if (extra.Count == 0)
        {
            return errorCode;
        }

        return string.IsNullOrWhiteSpace(errorCode)
            ? string.Join(';', extra)
            : errorCode.TrimEnd(';', ' ') + ";" + string.Join(';', extra);
    }

    /// <summary>Mensaje de novedad (null si no hay advertencias visibles).</summary>
    internal static string? Mensaje(string? errorCode)
    {
        var visibles = Visibles(errorCode);
        if (visibles.Count == 0)
        {
            return null;
        }

        // Tope para que el mensaje del webhook (message_validation varchar(500)) nunca falle por longitud.
        var mensaje = Prefijo + string.Join(", ", visibles.Select(v =>
            TextoDeAdvertencia.TryGetValue(v, out var texto) ? $"{texto} ({v})" : v));
        return mensaje.Length <= MaxLongitud ? mensaje : mensaje[..MaxLongitud];
    }


    /// <summary>
    /// Mensaje claro para los códigos de error FATALES de core-api al crear el borrador por el organismo de
    /// tránsito (gate de creación y OT derivado del RUNT). El código original se conserva entre paréntesis
    /// para trazabilidad (sin PII). Null si el código no es uno de estos (se usa tal cual).
    /// </summary>
    internal static string? MensajeError(string? errorCode)
    {
        var texto = errorCode?.Trim() switch
        {
            "ot_runt_no_habilitado" =>
                "El organismo de tránsito del vehículo según el RUNT no está activo o no está habilitado para la compañía",
            "ot_not_permitted" =>
                "El organismo de tránsito no está habilitado para la compañía",
            "OT_NOT_AUTHORIZED_FOR_TYPE" =>
                "El organismo de tránsito no está habilitado u operativo para este tipo de trámite",
            "tenant_inactive" =>
                "La compañía u organismo de tránsito no está activo",
            "network_inactive" =>
                "La red del organismo de tránsito no está activa",
            "placa_fuera_de_rango" =>
                "La placa enviada no pertenece al rango asignado por el organismo de tránsito a la compañía o ya no está disponible",
            "placa_rango_sin_ot" =>
                "No se pudo validar la placa del rango porque no se conoce el organismo de tránsito del trámite",
            _ => null,
        };

        return texto is null ? null : $"{texto} ({errorCode!.Trim()})";
    }
    /// <summary>Longitud máxima del mensaje (deja margen dentro de message_validation varchar(500)).</summary>
    internal const int MaxLongitud = 400;

    /// <summary>
    /// Deja la novedad en los dos comentarios del master que ya se muestran al consultar el pre-trámite:
    /// <c>business_comments_validation</c> es el <c>comments</c> del GET de estado que ve el tercero
    /// (<c>IctStatusV2Query</c>) y <c>external_comments_validation</c> alimenta el <c>MensajeNovedad</c> del
    /// recorrido de la bandeja (<c>DbRecorridoTramiteRepository</c>). No cambia el estado: el borrador
    /// existe (process_status_id = 5) y el gestor lo completa en FLIT.
    /// </summary>
    internal static void Aplicar(ExternalIntegrationMaster master, string mensaje)
    {
        ArgumentNullException.ThrowIfNull(master);
        master.BusinessCommentsValidation = Agregar(master.BusinessCommentsValidation, mensaje);
        master.ExternalCommentsValidation = Agregar(master.ExternalCommentsValidation, mensaje);
    }

    private static string Agregar(string? actual, string mensaje) =>
        ((actual ?? string.Empty) + " " + mensaje + ";").Trim();
}

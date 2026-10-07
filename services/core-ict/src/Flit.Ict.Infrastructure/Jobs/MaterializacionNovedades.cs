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
/// <c>"seed_warning:x;actors_warning:invalid_document_type"</c>. Solo se publican las de actores y
/// comerciales: las demás (seed/identity/attachments/preflight) no dejan el trámite sin datos que ICT
/// haya recibido. Los códigos no llevan PII (son códigos de error y, como mucho, el rol del actor).
/// </remarks>
internal static class MaterializacionNovedades
{
    private static readonly string[] PrefijosVisibles = ["actors_warning:", "commercial_warning:"];

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

    /// <summary>Mensaje de novedad (null si no hay advertencias visibles).</summary>
    internal static string? Mensaje(string? errorCode)
    {
        var visibles = Visibles(errorCode);
        if (visibles.Count == 0)
        {
            return null;
        }

        // Tope para que el mensaje del webhook (message_validation varchar(500)) nunca falle por longitud.
        var mensaje = Prefijo + string.Join(", ", visibles);
        return mensaje.Length <= MaxLongitud ? mensaje : mensaje[..MaxLongitud];
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

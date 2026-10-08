using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Problema HTTP (status, title y detail) de un código de error de los generadores de consolidado.</summary>
public sealed record ConsolidadoProblema(int StatusCode, string Title, string Detail);

/// <summary>
/// HU #13375 (Épica #13216) — catálogo ÚNICO de textos de los códigos de error del consolidado, con dos variantes:
/// <list type="bullet">
///   <item><see cref="Problema"/>: el ProblemDetails del POST de generación y del GET de entrega (extraído tal cual
///   de <c>ConsolidadoEndpoints.ProblemFor</c>, que lo consume sin cambio de contrato).</item>
///   <item><see cref="ParaLote"/>: el motivo legible de cada omisión del lote de descarga masiva (columna
///   <c>omission_reason</c> y <c>omitidos.csv</c>). Redactado para un CSV: sin instrucciones de pantalla
///   («Debe generar el FUR…») ni códigos técnicos.</item>
/// </list>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var p = ConsolidadoErrorTextos.Problema("sin_adjuntos");            // 409 · Conflict · "No hay adjuntos para consolidar."
/// var t = ConsolidadoErrorTextos.ParaLote(ConsolidadoLoteOmisiones.AccesoRevocado); // "Acceso revocado"
/// </code>
/// </remarks>
public static class ConsolidadoErrorTextos
{
    private const string Conflict = "Conflict";

    /// <summary>Texto del CSV de omitidos por código de <see cref="ConsolidadoLoteOmisiones"/> (los once).</summary>
    private static readonly Dictionary<string, string> TextosLote = new(StringComparer.Ordinal)
    {
        [ConsolidadoLoteOmisiones.FurRequerido] = "El trámite no tiene FUR; no se pudo generar el consolidado",
        [ConsolidadoLoteOmisiones.MigradoSoloLectura] = "Trámite migrado sin consolidado",
        [ConsolidadoLoteOmisiones.SinAdjuntos] = "No hay adjuntos para consolidar",
        [ConsolidadoLoteOmisiones.AdjuntoNoDisponible] = "Un adjunto del expediente no está disponible en almacenamiento",
        [ConsolidadoLoteOmisiones.MimetypeNoSoportado] = "Un adjunto tiene un formato no soportado para el consolidado",
        [ConsolidadoLoteOmisiones.OrganismoRequerido] =
            "El organismo de tránsito del trámite no está seleccionado o no está activo en el sistema",
        [ConsolidadoLoteOmisiones.ModalidadNoSoportada] =
            "El consolidado solo está disponible para matrícula inicial y traspaso",
        [ConsolidadoLoteOmisiones.QuipuxSoloLectura] = "Organismo en modo Quipux de solo lectura",
        [ConsolidadoLoteOmisiones.AccesoRevocado] = "Acceso revocado",
        [ConsolidadoLoteOmisiones.ErrorTecnico] = "No se pudo generar el consolidado, intente de nuevo",
        // HU #13418 AC2 (adenda v7, P1 = a): literal del AC; la red es de solo consulta y el lote no genera en la hija.
        [ConsolidadoLoteOmisiones.RedSinConsolidado] = "La compañía no ha generado su consolidado",
    };

    /// <summary>
    /// ProblemDetails de un código de error del generador. Cualquier código no catalogado viaja tal cual en el
    /// <c>detail</c> con 409: es mejor un motivo técnico que un éxito falso.
    /// </summary>
    public static ConsolidadoProblema Problema(string error) => error switch
    {
        "not_found" => new(404, "Not Found", "Procedure instance not found."),
        "migrado_solo_lectura" => new(409, Conflict, "Trámite migrado (solo lectura): no se regenera el consolidado."),
        "modalidad_no_soportada" => new(409, Conflict, "El consolidado solo está disponible para matrícula inicial y traspaso."),
        SubmitGate.FurRequerido => new(409, Conflict, "Debe generar el FUR antes del consolidado."),
        "sin_adjuntos" => new(409, Conflict, "No hay adjuntos para consolidar."),
        "adjunto_no_disponible" => new(409, Conflict, "Un adjunto del expediente no está disponible en almacenamiento."),
        "mimetype_no_soportado" => new(409, Conflict, "Un adjunto tiene un formato no soportado para el consolidado."),
        "storage_unavailable" => new(
            503,
            "Service Unavailable",
            "No se pudo guardar el consolidado en el almacenamiento de archivos. Intenta de nuevo en unos minutos."),
        "organismo_requerido" => new(
            409,
            Conflict,
            "El organismo de tránsito del trámite no está seleccionado o no está activo "
                + "en el sistema. Verifícalo antes de generar el expediente consolidado."),
        _ => new(409, Conflict, $"No se pudo generar el expediente consolidado: {error}."),
    };

    /// <summary>Motivo legible de una omisión del lote (CSV de omitidos).</summary>
    /// <exception cref="ArgumentException">El código no es del vocabulario <see cref="ConsolidadoLoteOmisiones"/>.</exception>
    public static string ParaLote(string codigoOmision) =>
        codigoOmision is not null && TextosLote.TryGetValue(codigoOmision, out var texto)
            ? texto
            : throw new ArgumentException(
                $"Código de omisión fuera del vocabulario del lote: '{codigoOmision}'.", nameof(codigoOmision));
}

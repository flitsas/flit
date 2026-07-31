using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Orden de prelación del expediente consolidado de matrícula inicial: FUR y certificados
/// generados primero, luego documentos del checklist en el orden del catálogo.
/// </summary>
internal static class MatriculaConsolidadoOrdering
{
    private static readonly string[] Precedence =
    [
        "fur",
        // Licencia de Tránsito emitida por el OT al decidir el trámite: entra al consolidado
        // justo después del FUR (generar o re-generar desde cualquier módulo la incluye).
        "licencia_transito",
        // ADR-0036 (HU #10915/#10914) — autorizaciones de radicación tras el FUR: el mandato
        // (condicional) y la solicitud de trámite virtual (siempre), antes de los certificados.
        "mandato",
        "tramite_virtual",
        "certificado_identidad",
        // HU #10589 — certificado RUES de la persona jurídica, tras el de identidad.
        "certificado_rues",
        // HU #10762 — certificado RNMC (medidas correctivas), junto a los demás certificados generados.
        "certificado_rnmc",
        // HU #10926 — escritura de la compañía (NIT) del actor, tras los certificados generados.
        "escritura",
        "escritura_comprador",
        "factura",
        "aduana",
        "impronta",
        "soat",
        "certificado_ambiental",
        "declaracion_aduana",
        "acta_remate",
        "oficio_judicial",
        "otro",
    ];

    // Ningún expediente consolidado puede ser PARTE de otro: se excluyen los dos tipos.
    // Faltaba `consolidado_maestro`, y por eso al aprobar el organismo de tránsito se duplicaba todo
    // el expediente: la aprobación genera el maestro (que ya contiene TODOS los documentos) e invalida
    // el consolidado del wizard, así que la siguiente regeneración lo mezclaba como un adjunto más y
    // cada documento salía dos veces.
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "consolidado",
        "consolidado_maestro",
        // En matrícula inicial no hay compraventa que anexar.
        "compraventa",
    };

    internal static IReadOnlyList<ProcedureInstanceAttachment> SelectOrdered(
        IEnumerable<ProcedureInstanceAttachment> attachments)
    {
        var rank = Precedence
            .Select((tipo, index) => (tipo, index))
            .ToDictionary(x => x.tipo, x => x.index, StringComparer.OrdinalIgnoreCase);

        return attachments
            .Where(a => !Excluded.Contains(a.Tipo))
            .Where(a => !a.Tipo.StartsWith("biometric_", StringComparison.OrdinalIgnoreCase))
            .Where(a => IsMergeableMime(a.Mimetype))
            .OrderBy(a => rank.TryGetValue(a.Tipo, out var r) ? r : Precedence.Length + 1)
            .ThenBy(a => a.UploadedAt)
            .ToList();
    }

    private static bool IsMergeableMime(string? mimetype) =>
        string.Equals(mimetype, "application/pdf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/jpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/png", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/webp", StringComparison.OrdinalIgnoreCase);
}

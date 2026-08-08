using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Orden de prelación del expediente consolidado de traspaso (HU #10455): FUR, certificado de
/// identidad y contrato de compraventa primero, luego los documentos del checklist. A diferencia
/// de <see cref="MatriculaConsolidadoOrdering"/>, la <c>compraventa</c> SÍ se incluye (es parte del
/// expediente de traspaso); solo se excluye el propio <c>consolidado</c>.
/// </summary>
internal static class TraspasoConsolidadoOrdering
{
    private static readonly string[] Precedence =
    [
        "fur",
        // Licencia de Tránsito emitida por el OT al decidir el trámite (misma posición que en matrícula).
        "licencia_transito",
        // ADR-0036 (HU #10915/#10914) — autorizaciones de radicación tras el FUR: el mandato
        // (condicional) y la solicitud de trámite virtual (siempre), antes de los certificados.
        "mandato",
        "tramite_virtual",
        "certificado_identidad",
        // Certificado de identidad del vendedor (traspaso), tras el del comprador.
        "certificado_identidad_vendedor",
        // HU #10589 — certificado RUES de la persona jurídica, tras el de identidad.
        "certificado_rues",
        // HU #11307 — el certificado RUES del VENDEDOR y el de vigencia SOAT/RTM faltaban en esta
        // lista: caían al final por defecto (rank = Precedence.Length + 1), mezclados con "otro" y
        // ordenados solo por fecha de carga. El expediente que ve el organismo de tránsito los
        // presentaba en un sitio arbitrario que además cambiaba entre regeneraciones.
        "certificado_rues_vendedor",
        "certificado_soat_rtm",
        // HU #10762 — certificado RNMC (medidas correctivas), junto a los demás certificados generados.
        "certificado_rnmc",
        "compraventa",
        // HU #10926 — escritura de la compañía (NIT) de cada actor, tras la compraventa: vendedor/
        // propietario ('escritura') y comprador ('escritura_comprador').
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

using System.Globalization;
using System.Text;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Tipo de servicio del vehículo — casilla 18 del FUR. El campo <c>vehicle_service</c> de
/// <c>field_values</c> tiene DOS orígenes:
/// <list type="bullet">
/// <item>Traspaso: lo hidrata el RUNT como TEXTO LIBRE ("Particular", "PUBLICO",
/// "SERVICIO PUBLICO ESPECIAL"…) — ver <c>KyverumRuntVehicleResultMapper</c>,
/// <c>IntempoVehicleResultMapper</c>, <c>VerifikResultMapper</c>.</item>
/// <item>Matrícula inicial: el operador elige un valor de este mismo catálogo cerrado y se
/// persiste el CÓDIGO (<see cref="Particular"/>..<see cref="Otros"/>).</item>
/// </list>
/// <see cref="Resolve"/> normaliza cualquiera de las dos formas a UN solo código canónico, para
/// que el generador del FUR marque exactamente una casilla (bug previo: un texto compuesto del
/// RUNT como "SERVICIO PUBLICO ESPECIAL" marcaba PÚBLICO y ESPECIAL a la vez).
/// </summary>
/// <remarks>
/// Se llama <c>VehicleServiceTypeCode</c> y NO <c>VehicleServiceType</c> a propósito: ese nombre ya
/// lo usa la entidad EF <c>Flit.Infrastructure.Persistence.Entities.Catalogs.VehicleServiceType</c>
/// (fila del catálogo <c>catalogs.vehicle_service_types</c> — Id/Code/Name/SortOrder) del trabajo en
/// paralelo de matrícula inicial. Son conceptos distintos —esta clase resuelve el CÓDIGO desde
/// texto libre u otro código, no representa una fila de catálogo— y compartir el nombre en el mismo
/// solution, en namespaces cercanos, habría sido una ambigüedad esperando a pasar en cualquier
/// archivo que importe ambos.
/// </remarks>
public static class VehicleServiceTypeCode
{
    public const string Particular = "PARTICULAR";
    public const string Publico = "PUBLICO";
    public const string Diplomatico = "DIPLOMATICO";
    public const string Oficial = "OFICIAL";
    public const string Especial = "ESPECIAL";
    public const string Otros = "OTROS";

    public static readonly IReadOnlyList<string> All =
    [
        Particular, Publico, Diplomatico, Oficial, Especial, Otros,
    ];

    /// <summary>
    /// Resuelve <paramref name="raw"/> (código de matrícula inicial o texto libre del RUNT) a UN
    /// código canónico, o <c>null</c> si el texto no es reconocible.
    /// <list type="bullet">
    /// <item><b>Vacío</b> ⇒ <see cref="Particular"/>. Comportamiento histórico preservado a
    /// propósito: hoy, cuando el campo nunca se pobló, el FUR ya sale marcando "Particular"; si
    /// aquí devolviéramos <c>null</c> cambiaríamos la salida de FURs que ya se generan así.</item>
    /// <item><b>Ya es uno de los 6 códigos</b> (sin distinguir mayúsculas/minúsculas, p. ej. desde
    /// matrícula inicial) ⇒ ese código exacto, sin pasar por la heurística de texto libre. El
    /// despojo de tildes de <see cref="Norm"/> es best-effort: este repositorio corre con
    /// <c>InvariantGlobalization=true</c>, donde <c>string.Normalize</c> no descompone
    /// diacríticos (mismo límite que ya tenía <c>FurFieldMapper.Norm</c>, del que esta función se
    /// copia). No es un problema real hoy: los 6 códigos y el texto que mandan los tres mappers del
    /// RUNT son siempre ASCII sin tildes.</item>
    /// <item><b>Texto libre del RUNT</b> ⇒ se resuelve por PRECEDENCIA determinista: las
    /// categorías ESPECÍFICAS (<see cref="Diplomatico"/>, <see cref="Oficial"/>,
    /// <see cref="Especial"/>) ganan sobre las GENÉRICAS (<see cref="Publico"/>,
    /// <see cref="Otros"/>, <see cref="Particular"/>). Motivo, con el caso real documentado
    /// ("SERVICIO PUBLICO ESPECIAL"): el RUNT antepone "PUBLICO" como calificador amplio y
    /// "ESPECIAL" como la clasificación específica que realmente importa — de hecho
    /// <see cref="Services.TramiteDocumentContextMapper"/> ya usa "contiene ESPECIAL" (RF33)
    /// para exigir documentos adicionales, sin condicionarlo a que también diga "público". Tratar
    /// el resultado como ESPECIAL (y no PÚBLICO) mantiene la casilla del FUR alineada con esa
    /// regla de negocio en vez de perder la información más específica. Diplomático y Oficial se
    /// evalúan con la misma prioridad que Especial (antes que Público/Otros/Particular) porque,
    /// igual que "especial", son calificativos jurídicamente más precisos que "público": si el
    /// RUNT los combinara con "público", el calificativo específico es el dato relevante para el
    /// trámite. Entre sí, Diplomático y Oficial no se combinan en la práctica, así que su orden
    /// relativo no tiene impacto observado; se dejan primero por ser los más inequívocos.</item>
    /// <item><b>Texto libre no reconocido</b> (ninguna palabra clave del catálogo) ⇒ <c>null</c>.
    /// Comportamiento histórico preservado: antes, un valor desconocido no marcaba ninguna
    /// casilla (todas las comparaciones por substring fallaban); con el normalizador, "ninguna
    /// casilla" se traduce en no resolver ningún código.</item>
    /// </list>
    /// </summary>
    public static string? Resolve(string? raw)
    {
        var n = Norm(raw);
        if (n.Length == 0)
            return Particular;

        // Camino rápido: el valor YA es uno de los 6 códigos cerrados (matrícula inicial).
        foreach (var code in All)
        {
            if (string.Equals(n, code, StringComparison.Ordinal))
                return code;
        }

        // Texto libre del RUNT: precedencia específico > genérico (ver comentario del método).
        if (n.Contains("DIPLOMAT", StringComparison.Ordinal))
            return Diplomatico;
        if (n.Contains("OFICIAL", StringComparison.Ordinal))
            return Oficial;
        if (n.Contains("ESPECIAL", StringComparison.Ordinal))
            return Especial;
        if (n.Contains("PUBLICO", StringComparison.Ordinal) || n.Contains("PUBLIC", StringComparison.Ordinal))
            return Publico;
        if (n.Contains("OTRO", StringComparison.Ordinal))
            return Otros;
        if (n.Contains("PARTICULAR", StringComparison.Ordinal) || n.Contains("PARTICUL", StringComparison.Ordinal))
            return Particular;

        return null;
    }

    private static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().ToUpperInvariant().Trim();
    }
}

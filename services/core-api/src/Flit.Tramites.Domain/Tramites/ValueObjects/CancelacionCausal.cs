using System;
using System.Collections.Generic;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>Causal declarada en un trámite <c>CANCELACION_MATRICULA</c>: por qué se cancela.</summary>
public enum CancelacionCausal
{
    /// <summary>Nada declarado todavía (o valor no reconocido).</summary>
    Ninguna,

    /// <summary>Un juez ordena la cancelación.</summary>
    DecisionJudicial,

    /// <summary>El vehículo se pierde totalmente por un hecho ajeno a la voluntad (incendio, inundación…).</summary>
    PerdidaTotalFuerzaMayor,

    /// <summary>El vehículo se pierde totalmente en un accidente de tránsito.</summary>
    PerdidaTotalAccidente,

    /// <summary>El propietario decide sacar el vehículo de circulación.</summary>
    DecisionVoluntaria,
}

/// <summary>
/// Fuente única de la causal de cancelación, compartida por el asistente (qué se ofrece), el
/// checklist (qué documentos se exigen) y el FUR (qué imprime en observaciones).
///
/// <para><b>Por qué existe.</b> La cancelación de matrícula tiene UNA casilla en el numeral 3 (la 13)
/// para cuatro trámites que el organismo trata distinto: no es lo mismo cancelar porque lo ordena un
/// juez que porque el vehículo se destruyó en un accidente, y cada causal se acredita con documentos
/// diferentes. Hasta ahora FLIT no preguntaba la causal, así que el checklist pedía lo mismo para las
/// cuatro y el formulario salía mudo sobre cuál era.</para>
///
/// <para>Sigue el mismo patrón que <see cref="BlindajeOpciones"/>: la opción vive en
/// <c>field_values</c> bajo <see cref="FieldKey"/>, el dominio la parsea y de ella se derivan tanto
/// los documentos exigidos como el texto del párrafo 23. Nada se deduce dos veces por separado.</para>
/// </summary>
public static class CancelacionCausales
{
    /// <summary>Tipo de trámite al que pertenece la causal (<c>procedure_types.code</c>).</summary>
    public const string TipoCodigo = "CANCELACION_MATRICULA";

    /// <summary>Clave de <c>field_values</c> donde el asistente persiste la causal.</summary>
    public const string FieldKey = "cancelacion_causal";

    public const string CodigoDecisionJudicial = "DECISION_JUDICIAL";
    public const string CodigoPerdidaTotalFuerzaMayor = "PERDIDA_TOTAL_FUERZA_MAYOR";
    public const string CodigoPerdidaTotalAccidente = "PERDIDA_TOTAL_ACCIDENTE";
    public const string CodigoDecisionVoluntaria = "DECISION_VOLUNTARIA";

    // Tipos de documento que acreditan cada causal (tramites.document_types.code). El acto de
    // decisión judicial reutiliza `oficio_judicial`, que ya existía en el catálogo y ya estaba
    // atado a este trámite: darle un código nuevo dejaría dos tipos casi idénticos conviviendo.
    public const string DocActoDecisionJudicial = "oficio_judicial";
    public const string DocCertificadoDijin = "certificado_dijin";
    public const string DocCertificadoAseguradoraPerito = "certificado_aseguradora_perito";
    public const string DocCertificadoAutoridadAdministrativa = "certificado_autoridad_administrativa";

    /// <summary>Códigos admitidos, en el orden en que se le ofrecen al gestor.</summary>
    public static readonly IReadOnlyList<string> Codigos =
    [
        CodigoDecisionJudicial,
        CodigoPerdidaTotalFuerzaMayor,
        CodigoPerdidaTotalAccidente,
        CodigoDecisionVoluntaria,
    ];

    /// <summary>
    /// Lee la causal persistida. Un valor ausente, vacío o no reconocido devuelve
    /// <see cref="CancelacionCausal.Ninguna"/>: no se adivina una causal a partir de un dato roto,
    /// porque de ella cuelgan los documentos que se exigen y lo que el FUR declara al organismo.
    /// </summary>
    public static CancelacionCausal Parse(string? valor) =>
        (valor ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            CodigoDecisionJudicial => CancelacionCausal.DecisionJudicial,
            CodigoPerdidaTotalFuerzaMayor => CancelacionCausal.PerdidaTotalFuerzaMayor,
            CodigoPerdidaTotalAccidente => CancelacionCausal.PerdidaTotalAccidente,
            CodigoDecisionVoluntaria => CancelacionCausal.DecisionVoluntaria,
            _ => CancelacionCausal.Ninguna,
        };

    /// <summary>Código canónico de una causal, o <c>null</c> para <see cref="CancelacionCausal.Ninguna"/>.</summary>
    public static string? ToCodigo(CancelacionCausal causal) => causal switch
    {
        CancelacionCausal.DecisionJudicial => CodigoDecisionJudicial,
        CancelacionCausal.PerdidaTotalFuerzaMayor => CodigoPerdidaTotalFuerzaMayor,
        CancelacionCausal.PerdidaTotalAccidente => CodigoPerdidaTotalAccidente,
        CancelacionCausal.DecisionVoluntaria => CodigoDecisionVoluntaria,
        _ => null,
    };

    /// <summary>
    /// Documentos OBLIGATORIOS de la causal. Todos los de la lista, no uno cualquiera de ellos: las
    /// dos pérdidas totales se acreditan con los tres certificados a la vez.
    ///
    /// <para>Sin causal declarada devuelve lista vacía — no se exige nada de más mientras el gestor
    /// no haya elegido, igual que no se inventa el texto del FUR. Los documentos de base del tipo
    /// (certificado de tradición) no salen de aquí: los pone el catálogo y siguen vigentes en las
    /// cuatro causales.</para>
    /// </summary>
    public static IReadOnlyList<string> DocumentosExigidos(CancelacionCausal causal) => causal switch
    {
        CancelacionCausal.DecisionJudicial => [DocActoDecisionJudicial],
        CancelacionCausal.PerdidaTotalFuerzaMayor or CancelacionCausal.PerdidaTotalAccidente =>
        [
            DocCertificadoDijin,
            DocCertificadoAseguradoraPerito,
            DocCertificadoAutoridadAdministrativa,
        ],
        CancelacionCausal.DecisionVoluntaria => [DocCertificadoDijin],
        _ => [],
    };

    /// <summary>Igual que <see cref="DocumentosExigidos(CancelacionCausal)"/> desde el valor crudo.</summary>
    public static IReadOnlyList<string> DocumentosExigidos(string? valorPersistido) =>
        DocumentosExigidos(Parse(valorPersistido));

    /// <summary>
    /// Todos los documentos que alguna causal puede exigir. Lo usa el catálogo de reglas para
    /// declarar una regla por documento y causal sin repetir la lista.
    /// </summary>
    public static readonly IReadOnlyList<string> TodosLosDocumentos =
    [
        DocActoDecisionJudicial,
        DocCertificadoDijin,
        DocCertificadoAseguradoraPerito,
        DocCertificadoAutoridadAdministrativa,
    ];

    /// <summary>Rótulo del documento en el checklist (mismo texto que el catálogo de la BD).</summary>
    public static string EtiquetaDocumento(string docTipo) => docTipo switch
    {
        DocActoDecisionJudicial => "Acto de decisión judicial",
        DocCertificadoDijin => "Certificado DIJIN o Policía",
        DocCertificadoAseguradoraPerito => "Certificado de aseguradora o perito",
        DocCertificadoAutoridadAdministrativa => "Certificado de autoridad administrativa",
        _ => docTipo,
    };

    /// <summary>¿La causal exige este documento?</summary>
    public static bool Exige(CancelacionCausal causal, string docTipo)
    {
        foreach (var d in DocumentosExigidos(causal))
        {
            if (string.Equals(d, docTipo, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

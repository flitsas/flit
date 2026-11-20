namespace Flit.Diagnostico.Domain.Diagnosticos;

/// <summary>
/// Plan comercial del diagnóstico (plan §5, R-2). En la base: <c>SIMPLE</c> o <c>CON_ANALISIS</c>. El modo Con Análisis IA
/// exige tipo de trámite (<c>ck_tipo_con_analisis</c>).
/// </summary>
public enum ModoDiagnostico
{
    /// <summary>Solo consultas, sin dictamen.</summary>
    Simple,

    /// <summary>Con Análisis IA: consultas y dictamen por tipo de trámite.</summary>
    ConAnalisis,
}

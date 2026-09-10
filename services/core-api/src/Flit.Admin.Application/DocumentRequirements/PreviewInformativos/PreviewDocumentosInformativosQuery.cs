namespace Flit.Admin.Application.DocumentRequirements.PreviewInformativos;

/// <summary>
/// Preview informativo de documentos requeridos por tipología de trámite (paso 1 del wizard).
/// No crea instancia ni checklist de carga: solo lista qué debe tener listo el operador.
/// </summary>
public sealed class PreviewDocumentosInformativosQuery
{
    /// <summary>
    /// Modalidad del wizard: <c>matricula_inicial</c> o <c>traspaso</c>
    /// (se mapea a tipología <c>traspaso_standard</c>).
    /// </summary>
    public required string Modalidad { get; init; }

    /// <summary>OT opcional: si viene, aplica overrides de la matriz OT &gt; Default.</summary>
    public Guid? TransitOfficeId { get; init; }
}

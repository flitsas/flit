using System.Diagnostics.CodeAnalysis;

namespace Flit.Tramites.Domain.Identity;

/// <summary>Motivo homologado de rechazo de una revisión manual: código estable y etiqueta en español para personas.</summary>
public sealed record ManualRejectionReason(string Code, string Label);

/// <summary>
/// HU #13299 (Feature #13282 C, Épica #13202) — LISTA CERRADA de motivos de rechazo de una validación manual (contrato
/// <c>EPICA-13202-contrato-api.md</c> §3). Es el ÚNICO lugar del backend con los códigos y sus etiquetas: el endpoint de catálogo
/// (<c>GET .../manual-rejection-reasons</c>), la validación del rechazo y el correo al cliente leen de aquí. Las etiquetas son las
/// mismas del front (<c>frontend/lib/identidad/motivos-rechazo-manual.ts</c>). Agregar un motivo = una línea aquí.
/// </summary>
public static class ManualRejectionReasons
{
    public const string ImagenBorrosa = "imagen_borrosa";
    public const string RostroNoCoincide = "rostro_no_coincide";
    public const string DocumentoIlegibleOIncompleto = "documento_ilegible_o_incompleto";
    public const string DocumentoNoCorresponde = "documento_no_corresponde";
    public const string FirmaIlegibleONoCorresponde = "firma_ilegible_o_no_corresponde";
    public const string CapturaFueraDeEncuadre = "captura_fuera_de_encuadre";

    public static readonly IReadOnlyList<ManualRejectionReason> Todos =
    [
        new(ImagenBorrosa, "Imagen borrosa"),
        new(RostroNoCoincide, "El rostro no coincide con el documento"),
        new(DocumentoIlegibleOIncompleto, "Documento ilegible o incompleto"),
        new(DocumentoNoCorresponde, "El documento no corresponde"),
        new(FirmaIlegibleONoCorresponde, "Firma ilegible o no corresponde"),
        new(CapturaFueraDeEncuadre, "Captura fuera de encuadre"),
    ];

    /// <summary>¿El código está en la lista cerrada? Comparación exacta (ordinal): no se normaliza mayúsculas ni espacios.</summary>
    public static bool IsValid([NotNullWhen(true)] string? code) =>
        code is not null && Todos.Any(r => string.Equals(r.Code, code, StringComparison.Ordinal));

    /// <summary>Etiqueta en español del código, o <c>null</c> si no está en la lista.</summary>
    public static string? LabelFor(string? code) =>
        code is null ? null : Todos.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.Ordinal))?.Label;
}

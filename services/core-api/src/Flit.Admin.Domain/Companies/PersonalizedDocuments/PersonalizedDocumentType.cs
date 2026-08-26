namespace Flit.Admin.Domain.Companies.PersonalizedDocuments;

/// <summary>
/// Vocabulario cerrado del tipo de documento personalizable por compañía (HU #11313, Feature #11309,
/// ADR-0042). Es el <b>mismo</b> vocabulario del <c>Tipo</c> del adjunto de trámite (<c>mandato</c> /
/// <c>tramite_virtual</c>) — no un catálogo nuevo: conservar el <c>Tipo</c> es lo que hace que el pie
/// de página, la matriz documental y el orden del expediente se hereden gratis (DT-1/DT-2 del plan
/// técnico). Cualquier otro valor es <c>400</c> en el alta (AC1).
/// </summary>
public static class PersonalizedDocumentTypes
{
    public const string Mandato = "mandato";
    public const string TramiteVirtual = "tramite_virtual";

    public static readonly IReadOnlyList<string> All = [Mandato, TramiteVirtual];

    public static bool IsValid(string? value) =>
        value is Mandato or TramiteVirtual;
}

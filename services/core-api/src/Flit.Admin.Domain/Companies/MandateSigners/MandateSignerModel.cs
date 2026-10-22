namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// HU #13129 (ADR-0061) — valores almacenados y de API del modelo del mandatario. No confundir con el
/// tipo de mandato compañía×OT (<c>assignment_mode</c>: signer, institutional, open).
/// </summary>
public static class MandateSignerModels
{
    public const string Natural = "natural";
    public const string Juridica = "juridica";
    public const string FormatoBlanco = "formato_blanco";

    /// <summary>Nombre que el sistema fija para el Formato en blanco (no hay persona detrás).</summary>
    public const string FormatoBlancoFullName = "Formato en blanco";

    public static bool IsValid(string? value) =>
        value is Natural or Juridica or FormatoBlanco;
}

/// <summary>Forma de firma de una Persona natural: <c>baul</c> o <c>biometria</c> (la firma física se retira en #13131).</summary>
public static class MandateSignatureMethods
{
    public const string Baul = "baul";
    public const string Biometria = "biometria";

    public static bool IsValid(string? value) => value is Baul or Biometria;
}

/// <summary>Tipo de vigencia propia del mandatario: <c>fixed</c> (sin fechas) o <c>range</c> (con fechas).</summary>
public static class MandateValidityKinds
{
    public const string Fixed = "fixed";
    public const string Range = "range";

    public static bool IsValid(string? value) => value is Fixed or Range;
}

/// <summary>
/// HU #13129 — estado de vigencia del mandatario, calculado en servidor y nunca persistido. Orden de
/// evaluación: inactivo → vencido → (no vigente aún) → por vencer → vigente. Las fechas son <c>date</c> y
/// «hoy» es el día calendario de Colombia (America/Bogota) que decide quien llama.
/// </summary>
public static class MandateValidityStatus
{
    public const string Vigente = "vigente";
    public const string PorVencer = "por_vencer";
    public const string Vencido = "vencido";
    public const string Inactivo = "inactivo";

    /// <summary>
    /// Rango que aún no empieza (<c>hoy &lt; valid_from</c>): el PO lo trata como no vigente y los cuatro
    /// estados no lo nombran, así que se expone con este valor aditivo (ver duda D-6 del ADR-0061).
    /// </summary>
    public const string NoVigente = "no_vigente";

    /// <summary>Días de antelación al fin del rango a partir de los cuales el estado es «por vencer».</summary>
    public const int DiasPorVencer = 7;

    public static string Compute(
        bool isActive,
        string? validityKind,
        DateOnly? validFrom,
        DateOnly? validTo,
        DateOnly today)
    {
        if (!isActive)
        {
            return Inactivo;
        }

        if (validityKind != MandateValidityKinds.Range || validTo is null)
        {
            return Vigente;
        }

        if (today > validTo.Value)
        {
            return Vencido;
        }

        if (validFrom is { } from && today < from)
        {
            return NoVigente;
        }

        // Rango de un solo día (inicio = fin): vigente ese día y vencido al siguiente (HU #13129 AC7); nunca
        // «por vencer», porque toda su duración cabe dentro de la ventana de aviso.
        if (validFrom == validTo)
        {
            return Vigente;
        }

        return validTo.Value.DayNumber - today.DayNumber <= DiasPorVencer ? PorVencer : Vigente;
    }
}

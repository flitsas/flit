namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Escenarios normativos del Documento de Transferencia de Dominio
/// (<c>docs/plantilla-transferencia-dominio.md</c> §3). Son MUTUAMENTE EXCLUYENTES en un mismo
/// documento: el usuario elige exactamente uno antes de capturar datos (VB-05).
///
/// <para>Los literales son los del CHECK <c>ck_standalone_documents_scenario</c> del DDL 105: la
/// base de datos es la fuente de verdad y aquí solo se nombran.</para>
///
/// <para><b>El escenario clasifica la operación jurídica, no la naturaleza de las personas.</b> En
/// los tres pueden participar personas naturales o jurídicas (§3, aviso del anexo).</para>
/// </summary>
public static class TransferScenario
{
    /// <summary>Traspaso ordinario, art. 5.3.2.1. Transferente + adquirente.</summary>
    public const string TraspasoOrdinario = "A";

    /// <summary>Transferencia unilateral leasing → locatario, art. 5.3.2.2. Solo la entidad financiera.</summary>
    public const string UnilateralLeasing = "B";

    /// <summary>Entidad financiera → tercero, art. 5.3.2.1 sin exenciones.</summary>
    public const string FinancieraATercero = "C";

    public static bool IsKnown(string? scenario) =>
        scenario is TraspasoOrdinario or UnilateralLeasing or FinancieraATercero;
}

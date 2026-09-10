namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Catálogo de <c>{{tipo_opcion_compra}}</c> del escenario B (anexo §5.5 y §8.2, cláusula segunda).
/// Determina qué soportes acompañan el trámite: con <see cref="Automatica"/> basta la copia del
/// contrato de leasing (art. 5.3.2.2, Parágrafo 1.º); en los otros dos casos se aporta además la
/// declaración de terminación o de ejercicio de la opción de compra.
///
/// <para>Un valor fuera de este catálogo bloquea con <c>VB-B-03</c>: el generador no sabría qué
/// soportes anunciar en la cláusula quinta y §10 regla #9 lo prohíbe expresamente.</para>
/// </summary>
public static class TransferPurchaseOption
{
    /// <summary>El locatario ejerció expresamente la opción de compra pactada.</summary>
    public const string Ejercida = "EJERCIDA";

    /// <summary>La opción se activó automáticamente (art. 5.3.2.2, Parágrafo 1.º).</summary>
    public const string Automatica = "AUTOMATICA";

    /// <summary>El contrato llegó a su término por cumplimiento del plazo pactado.</summary>
    public const string TerminacionContrato = "TERMINACION_CONTRATO";

    public static bool IsKnown(string? opcion) =>
        opcion is Ejercida or Automatica or TerminacionContrato;

    /// <summary>
    /// ¿Se requiere la declaración de terminación / ejercicio de la opción, además del contrato?
    /// Con la opción automática NO: el Parágrafo 1.º se conforma con el contrato de leasing.
    /// </summary>
    public static bool RequiereDeclaracionAdicional(string? opcion) => opcion != Automatica;

    /// <summary>Redacción de la causal en la cláusula segunda del anexo §8.2.</summary>
    public static string Wording(string? opcion) => opcion switch
    {
        Ejercida => "el locatario ejerció expresamente la opción de compra pactada en el contrato de "
            + "leasing.",
        Automatica => "la opción de compra se activó automáticamente conforme a las condiciones del "
            + "contrato, sin necesidad de declaración expresa del locatario (art. 5.3.2.2, Parágrafo "
            + "1.º). Para este caso basta la copia del contrato de leasing como soporte; no se "
            + "requiere declaración adicional.",
        TerminacionContrato => "el contrato de leasing llegó a su término por cumplimiento del plazo "
            + "pactado.",
        _ => string.Empty,
    };
}

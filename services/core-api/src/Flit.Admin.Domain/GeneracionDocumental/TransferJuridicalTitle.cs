namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Catálogo de <c>{{titulo_juridico}}</c> (anexo §5.4). El art. 5.3.2.1 numeral 1.º admite
/// «contrato de compraventa, <b>documento o declaración</b> en el que conste la transferencia»: el
/// precio NO es requisito universal, lo es de la compraventa.
/// </summary>
public static class TransferJuridicalTitle
{
    public const string Compraventa = "COMPRAVENTA";
    public const string DacionEnPago = "DACION_EN_PAGO";
    public const string Permuta = "PERMUTA";
    public const string Donacion = "DONACION";
    public const string Otro = "OTRO";

    public static bool IsKnown(string? title) =>
        title is Compraventa or DacionEnPago or Permuta or Donacion or Otro;

    /// <summary>
    /// Solo la compraventa exige precio en letras Y en números (VB-A-06). Los demás títulos
    /// describen la contraprestación según la naturaleza del negocio, o no la tienen (donación).
    /// </summary>
    public static bool RequiresPrice(string? title) => title == Compraventa;

    /// <summary>Redacción de la cláusula segunda del anexo §8.1 para cada título.</summary>
    public static string Wording(string? title) => title switch
    {
        Compraventa => "contrato de compraventa",
        DacionEnPago => "dación en pago",
        Permuta => "permuta",
        Donacion => "donación",
        _ => "documento o declaración de transferencia de dominio",
    };
}

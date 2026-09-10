using Flit.Admin.Application.GeneracionDocumental.Ports;

namespace Flit.Infrastructure.Documents.Standalone;

/// <summary>
/// Redacciones compartidas por los escenarios del Documento de Transferencia de Dominio
/// (<c>docs/plantilla-transferencia-dominio.md</c> §8).
///
/// <para><b>Existen aquí y no duplicadas en cada escenario</b> porque el anexo las define una sola
/// vez: la identificación de una parte, la contraprestación y la cláusula fiscal son idénticas en A
/// y en C, y una copia divergente sería una diferencia normativa introducida por descuido. Lo que
/// NO se comparte es la estructura del documento: cada escenario decide qué cláusulas existen y
/// cuántas partes comparecen.</para>
/// </summary>
internal static class TransferClausulas
{
    /// <summary>
    /// Identificación de una parte dentro de la cláusula de comparecientes. Los tramos condicionales
    /// del anexo —DV y representante legal— solo se imprimen cuando hay dato: el checklist §13.1
    /// prohíbe que una etiqueta de renderización condicional o una variable sin resolver aparezcan
    /// en el PDF.
    /// </summary>
    public static string Parte(TransferDocumentParty parte, string rol)
    {
        var texto = $"{parte.NombreRazonSocial}, identificado(a) con {parte.Identificacion}";

        if (!string.IsNullOrWhiteSpace(parte.RepresentanteLegal))
        {
            texto += $", representado(a) legalmente por {parte.RepresentanteLegal}";

            if (!string.IsNullOrWhiteSpace(parte.CcRepresentanteLegal))
            {
                texto += $", identificado(a) con C.C. No. {parte.CcRepresentanteLegal}";
            }
        }

        texto += $" (en adelante «{rol}»)";

        if (!string.IsNullOrWhiteSpace(parte.Domicilio))
        {
            texto += $", con domicilio en {parte.Domicilio}";
        }

        return texto;
    }

    /// <summary>Cláusula de transferencia de dominio de los escenarios A y C (§8.1 y §8.3).</summary>
    public static string TransferenciaDeDominio(TransferDocumentBusiness negocio)
    {
        var titulo = negocio.TituloJuridico == "OTRO" && !string.IsNullOrWhiteSpace(negocio.DescripcionTitulo)
            ? negocio.DescripcionTitulo
            : negocio.TituloRedaccion;

        return "EL TRANSFERENTE, siendo propietario registrado del vehículo descrito en la cláusula "
            + "primera, transfiere el pleno dominio, la posesión y la propiedad de dicho vehículo a EL "
            + $"ADQUIRENTE mediante {titulo}, de conformidad con las normas civiles y/o mercantiles "
            + "vigentes (art. 5.3.2.1 numeral 1.º de la Resolución 20233040017145 de 2023).";
    }

    /// <summary>
    /// Contraprestación. El precio NO es universal: lo exige la compraventa (VB-A-06), mientras que
    /// permuta o dación en pago describen la contraprestación y la donación no tiene ninguna.
    /// </summary>
    public static string Contraprestacion(TransferDocumentBusiness negocio)
    {
        if (!string.IsNullOrWhiteSpace(negocio.PrecioLetras) && !string.IsNullOrWhiteSpace(negocio.PrecioNumeros))
        {
            var texto = $"La contraprestación acordada es {negocio.PrecioLetras} pesos "
                + $"($ {negocio.PrecioNumeros} COP)";

            return string.IsNullOrWhiteSpace(negocio.FormaPago)
                ? texto + "."
                : texto + $", que EL ADQUIRENTE paga o pagará de la siguiente forma: {negocio.FormaPago}.";
        }

        return string.IsNullOrWhiteSpace(negocio.ContraprestacionDescripcion)
            ? string.Empty
            : $"La contraprestación consiste en: {negocio.ContraprestacionDescripcion}.";
    }

    /// <summary>Inciso final de la cláusula segunda cuando hay gravamen declarado (§8.1).</summary>
    public static string Gravamen(TransferDocumentEncumbrance gravamen) =>
        gravamen.GravamenActivo
            ? "La presente transferencia se realiza adjuntando el documento en el que consta el "
              + "levantamiento o la autorización otorgada por el beneficiario del gravamen o limitación "
              + "para continuar con el nuevo propietario, de conformidad con el art. 5.3.2.1 numeral 3.º "
              + "de la Resolución 20233040017145 de 2023."
            : string.Empty;

    /// <summary>
    /// Primer párrafo de la cláusula fiscal (§8.1 y §8.3, cláusula SEXTA), común a A y C: el
    /// art. 5.3.2.2 tampoco exime el numeral 5.º, pero el escenario B no tiene cláusula fiscal
    /// porque no hay negocio entre las partes del instrumento.
    /// </summary>
    public const string CargasFiscales =
        "Las partes declaran conocer que el Organismo de Tránsito verificará el pago de la retención "
        + "en la fuente, el pago del impuesto sobre vehículos automotores y el pago de los derechos "
        + "del trámite a favor del Ministerio de Transporte, de la tarifa RUNT y de los derechos del "
        + "propio Organismo de Tránsito, conforme al art. 5.3.2.1 numeral 5.º de la Resolución "
        + "20233040017145 de 2023.";

    /// <summary>
    /// Segundo párrafo de la cláusula fiscal. En remolques y semirremolques se OMITE quién asume el
    /// impuesto: están exentos y no hay quién lo asuma (anexo §8.1, inciso condicional).
    /// </summary>
    public static string Asuncion(TransferDocumentBusiness negocio)
    {
        var impuesto = string.IsNullOrWhiteSpace(negocio.AsumeImpuestoVehiculo)
            ? string.Empty
            : $"el impuesto sobre vehículos automotores por {Sujeto(negocio.AsumeImpuestoVehiculo)}, ";

        return "Para efectos internos entre las partes y sin que ello altere la obligación legal frente a "
            + $"la administración, la retención en la fuente será asumida por {Sujeto(negocio.AsumeRetencionFuente)}, "
            + impuesto
            + "y los derechos del trámite, la tarifa RUNT y los derechos del Organismo de Tránsito por "
            + $"{Sujeto(negocio.AsumeDerechosTramite)}.";
    }

    /// <summary>
    /// Inciso de remolques y semirremolques. La exención de la Ley 488/1998 es de <b>impuesto</b>, no
    /// de SOAT: el anexo corrigió esa atribución (§2, §4.1 y §6.2) y el PDF no puede reintroducirla.
    /// </summary>
    public static string Remolque(string? claseVehiculo) =>
        claseVehiculo?.Contains("REMOLQUE", StringComparison.OrdinalIgnoreCase) == true
            ? "Tratándose de un remolque o semirremolque, el Organismo de Tránsito no verifica el pago del "
              + "impuesto sobre vehículos, por encontrarse exento conforme a la Ley 488 de 1998 "
              + "(art. 5.3.2.1 numeral 5.º, inciso final)."
            : string.Empty;

    /// <summary>Traducción del catálogo de §5.4 a sujeto legible dentro de la cláusula.</summary>
    public static string Sujeto(string valor) => valor switch
    {
        "TRANSFERENTE" => "EL TRANSFERENTE",
        "ADQUIRENTE" => "EL ADQUIRENTE",
        "COMPARTIDOS" => "ambas partes en proporciones iguales",
        _ => "quien la ley determine",
    };
}

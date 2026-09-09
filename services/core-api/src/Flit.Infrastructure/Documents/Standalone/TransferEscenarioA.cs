using Flit.Admin.Application.GeneracionDocumental.Ports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Standalone;

/// <summary>
/// Escenario A — traspaso ordinario del art. 5.3.2.1 (anexo §8.1 y §9.1). Arma el cuerpo del
/// documento con los bloques reutilizables de <see cref="TransferDocumentBlocks"/>.
///
/// <para><b>Este escenario instancia DOS bloques de firma</b> porque comparecen dos partes. No hay
/// una plantilla de dos columnas con una apagable: se le pasa al bloque de firmas la lista de partes
/// del modelo, que aquí trae transferente y adquirente. En el escenario B (HU-06) esa misma llamada
/// recibirá una lista de un elemento, y por eso el bloque del locatario no podrá existir (§9.0.3).</para>
/// </summary>
internal static class TransferEscenarioA
{
    public static void Compose(ColumnDescriptor col, TransferDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var transferente = model.ParteConRol(TransferPartyRole.Transferente)
            ?? throw new InvalidOperationException("El escenario A exige un transferente.");
        var adquirente = model.ParteConRol(TransferPartyRole.Adquirente)
            ?? throw new InvalidOperationException("El escenario A exige un adquirente.");
        var negocio = model.Negocio
            ?? throw new InvalidOperationException("El escenario A exige los datos del negocio.");

        TransferDocumentBlocks.Encabezado(col, model);

        TransferDocumentBlocks.Clausula(
            col,
            "COMPARECIENTES",
            Comparecientes(transferente, adquirente),
            "Hemos acordado celebrar el presente documento de transferencia de dominio, que se regirá "
            + "por las siguientes cláusulas:");

        TransferDocumentBlocks.Clausula(
            col,
            "PRIMERA — IDENTIFICACIÓN DEL VEHÍCULO",
            "Las partes declaran que el vehículo objeto de la presente transferencia tiene las "
            + "siguientes características registradas en el RNA:");
        TransferDocumentBlocks.TablaVehiculo(col, model.Vehiculo);

        TransferDocumentBlocks.Clausula(
            col,
            "SEGUNDA — TRANSFERENCIA DE DOMINIO",
            Transferencia(negocio),
            Contraprestacion(negocio),
            Gravamen(model.Gravamen));

        TransferDocumentBlocks.Clausula(
            col,
            "TERCERA — TRADICIÓN Y ENTREGA",
            "EL TRANSFERENTE hace entrega material del vehículo y de todos los documentos pertinentes "
            + "para que EL ADQUIRENTE pueda adelantar el trámite de traspaso ante el organismo de "
            + "tránsito competente del RNA, conforme a la Resolución 20233040017145 de 2023, "
            + "art. 5.3.2.1.");

        TransferDocumentBlocks.Clausula(
            col,
            "CUARTA — DECLARACIONES DEL TRANSFERENTE",
            "EL TRANSFERENTE declara bajo la gravedad del juramento: (i) que es el legítimo propietario "
            + "del vehículo; (ii) que el vehículo no se encuentra sujeto a medidas cautelares, embargos "
            + "ni limitaciones de dominio que impidan la presente transferencia, distintas de las "
            + "expresamente señaladas en la cláusula segunda; y (iii) que la información suministrada al "
            + "RNA es fiel reflejo de la situación jurídica y técnica del vehículo a la fecha de este "
            + "documento.");

        TransferDocumentBlocks.Clausula(
            col,
            "QUINTA — OBLIGACIONES REGISTRALES",
            "Las partes se obligan a adelantar, dentro de los términos legales, el trámite de traspaso "
            + "ante el organismo de tránsito, presentando este documento junto con el Formato Único de "
            + "Solicitud de Trámite (Anexo 46) y los demás requisitos del art. 5.3.2.1 de la Resolución "
            + "20233040017145 de 2023.");

        // Cláusula SEXTA del anexo §8.1: es la que IMPRIME las tres variables fiscales de §5.4. Sin
        // ella, {{asume_retencion_fuente}}, {{asume_impuesto_vehiculo}} y {{asume_derechos_tramite}}
        // se capturarían y jamás saldrían en el PDF.
        TransferDocumentBlocks.Clausula(
            col,
            "SEXTA — RETENCIÓN EN LA FUENTE, IMPUESTOS Y DERECHOS DEL TRÁMITE",
            "Las partes declaran conocer que el Organismo de Tránsito verificará el pago de la retención "
            + "en la fuente, el pago del impuesto sobre vehículos automotores y el pago de los derechos "
            + "del trámite a favor del Ministerio de Transporte, de la tarifa RUNT y de los derechos del "
            + "propio Organismo de Tránsito, conforme al art. 5.3.2.1 numeral 5.º de la Resolución "
            + "20233040017145 de 2023.",
            Asuncion(negocio),
            Remolque(model.Vehiculo.ClaseVehiculo));

        TransferDocumentBlocks.Firmas(col, model.CiudadFirma, model.FechaFirma, model.Partes);
        TransferDocumentBlocks.AdvertenciaNormativa(col);
    }

    private static string Comparecientes(TransferDocumentParty transferente, TransferDocumentParty adquirente) =>
        "Que entre los suscritos, de una parte, " + Parte(transferente, "EL TRANSFERENTE")
        + "; y de otra parte, " + Parte(adquirente, "EL ADQUIRENTE") + ".";

    /// <summary>
    /// Identificación de una parte. Los tramos condicionales del anexo —DV y representante legal—
    /// solo se imprimen cuando hay dato: el checklist §13.1 prohíbe que las etiquetas de
    /// renderización condicional o una variable sin resolver aparezcan en el PDF.
    /// </summary>
    private static string Parte(TransferDocumentParty parte, string rol)
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

    private static string Transferencia(TransferDocumentBusiness negocio)
    {
        var titulo = negocio.TituloJuridico == "OTRO" && !string.IsNullOrWhiteSpace(negocio.DescripcionTitulo)
            ? negocio.DescripcionTitulo
            : negocio.TituloRedaccion;

        return "EL TRANSFERENTE, siendo propietario registrado del vehículo descrito en la cláusula "
            + $"primera, transfiere el pleno dominio, la posesión y la propiedad de dicho vehículo a EL "
            + $"ADQUIRENTE mediante {titulo}, de conformidad con las normas civiles y/o mercantiles "
            + "vigentes (art. 5.3.2.1 numeral 1.º de la Resolución 20233040017145 de 2023).";
    }

    /// <summary>
    /// Contraprestación. El precio NO es universal: lo exige la compraventa (VB-A-06), mientras que
    /// permuta o dación en pago describen la contraprestación y la donación no tiene ninguna.
    /// </summary>
    private static string Contraprestacion(TransferDocumentBusiness negocio)
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

    private static string Gravamen(TransferDocumentEncumbrance gravamen) =>
        gravamen.GravamenActivo
            ? "La presente transferencia se realiza adjuntando el documento en el que consta el "
              + "levantamiento o la autorización otorgada por el beneficiario del gravamen o limitación "
              + "para continuar con el nuevo propietario, de conformidad con el art. 5.3.2.1 numeral 3.º "
              + "de la Resolución 20233040017145 de 2023."
            : string.Empty;

    /// <summary>
    /// Segundo párrafo de la cláusula sexta. En remolques y semirremolques se OMITE la mención del
    /// impuesto: están exentos y no hay quién lo asuma (anexo §8.1, inciso condicional).
    /// </summary>
    private static string Asuncion(TransferDocumentBusiness negocio)
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

    private static string Remolque(string? claseVehiculo) =>
        claseVehiculo?.Contains("REMOLQUE", StringComparison.OrdinalIgnoreCase) == true
            ? "Tratándose de un remolque o semirremolque, el Organismo de Tránsito no verifica el pago del "
              + "impuesto sobre vehículos, por encontrarse exento conforme a la Ley 488 de 1998 "
              + "(art. 5.3.2.1 numeral 5.º, inciso final)."
            : string.Empty;

    /// <summary>Traducción del catálogo de §5.4 a sujeto legible dentro de la cláusula.</summary>
    private static string Sujeto(string valor) => valor switch
    {
        "TRANSFERENTE" => "EL TRANSFERENTE",
        "ADQUIRENTE" => "EL ADQUIRENTE",
        "COMPARTIDOS" => "ambas partes en proporciones iguales",
        _ => "quien la ley determine",
    };
}

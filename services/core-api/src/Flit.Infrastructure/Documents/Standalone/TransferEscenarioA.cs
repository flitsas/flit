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
            TransferClausulas.TransferenciaDeDominio(negocio),
            TransferClausulas.Contraprestacion(negocio),
            TransferClausulas.Gravamen(model.Gravamen));

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
            TransferClausulas.CargasFiscales,
            TransferClausulas.Asuncion(negocio),
            TransferClausulas.Remolque(model.Vehiculo.ClaseVehiculo));

        TransferDocumentBlocks.Firmas(col, model.CiudadFirma, model.FechaFirma, model.Partes);
        TransferDocumentBlocks.AdvertenciaNormativa(col);
    }

    private static string Comparecientes(TransferDocumentParty transferente, TransferDocumentParty adquirente) =>
        "Que entre los suscritos, de una parte, " + TransferClausulas.Parte(transferente, "EL TRANSFERENTE")
        + "; y de otra parte, " + TransferClausulas.Parte(adquirente, "EL ADQUIRENTE") + ".";
}

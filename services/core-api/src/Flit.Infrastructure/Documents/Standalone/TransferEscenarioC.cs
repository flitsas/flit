using Flit.Admin.Application.GeneracionDocumental.Ports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Standalone;

/// <summary>
/// Escenario C — transferencia de entidad financiera a un tercero que NO es el locatario del
/// contrato de leasing (anexo §8.3 y §9.3). Se rige <b>íntegramente</b> por el art. 5.3.2.1.
///
/// <para><b>VB-C-07 — el escenario C no hereda exenciones.</b> El tercero es un comprador ordinario:
/// se le exigen RTM, QR/certificación de guarismos/improntas, paz y salvo de infracciones y su firma
/// en el Formato Único. Por eso este documento <b>no invoca ninguna exención del art. 5.3.2.2</b>, y
/// <see cref="VerificarSinExenciones"/> lo comprueba sobre los párrafos efectivamente compuestos
/// antes de que el PDF exista: una plantilla que incluyera la cláusula de exenciones falla la
/// verificación en vez de emitir un documento que prometa al tercero algo que la norma no le
/// concede.</para>
///
/// <para>La cláusula segunda sí <b>menciona</b> el art. 5.3.2.2 para declarar que no aplica. Eso no
/// es invocar una exención: es lo contrario, y el anexo §13.4 lo exige expresamente.</para>
///
/// <para>El bloque de firmas es <b>bilateral</b> —transferente y adquirente— con la nota de régimen
/// del anexo §9.3.</para>
/// </summary>
internal static class TransferEscenarioC
{
    /// <summary>
    /// Marcas de una invocación de exenciones del art. 5.3.2.2. Son las del texto del escenario B:
    /// la enumeración de las cinco excepciones y el encabezado de su cláusula cuarta. Lo que se
    /// persigue es la <b>concesión</b> de la exención al tercero, no la mención del artículo.
    /// </summary>
    private static readonly string[] MarcasDeExencion =
    [
        "EXENCIONES NORMATIVAS APLICABLES",
        "no se exigen:",
        "excepción 1.ª",
        "excepción 2.ª",
        "excepción 3.ª",
        "excepción 4.ª",
        "excepción 5.ª",
        "no se requiere revisión técnico-mecánica",
        "no se requieren improntas",
    ];

    public static void Compose(ColumnDescriptor col, TransferDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var transferente = model.ParteConRol(TransferPartyRole.Transferente)
            ?? throw new InvalidOperationException("El escenario C exige un transferente.");
        var adquirente = model.ParteConRol(TransferPartyRole.Adquirente)
            ?? throw new InvalidOperationException("El escenario C exige un adquirente tercero.");
        var negocio = model.Negocio
            ?? throw new InvalidOperationException("El escenario C exige los datos del negocio.");

        var comparecientes =
            "Que entre los suscritos, de una parte, "
            + TransferClausulas.Parte(transferente, "EL TRANSFERENTE") + " (entidad financiera); y de "
            + "otra parte, " + TransferClausulas.Parte(adquirente, "EL ADQUIRENTE") + ".";

        var regimen =
            "EL TRANSFERENTE es el propietario registrado del vehículo descrito ante el RNA. EL "
            + "ADQUIRENTE es un tercero distinto del locatario histórico del contrato de leasing "
            + "anterior. En consecuencia, la presente operación se rige íntegramente por el "
            + "art. 5.3.2.1 de la Resolución 20233040017145 de 2023, sin que apliquen las exenciones "
            + "previstas en el art. 5.3.2.2, las cuales son exclusivas del locatario beneficiario de "
            + "la opción de compra.";

        var requisitosPlenos =
            "Las partes reconocen que este trámite exige, además de este documento y el Formato Único "
            + "de Solicitud de Trámite (Anexo 46), los requisitos del art. 5.3.2.1 de la Resolución "
            + "20233040017145 de 2023, incluyendo: imagen del código QR, certificación de guarismos o "
            + "improntas; SOAT vigente; revisión técnico-mecánica vigente si aplica al tipo de "
            + "vehículo; verificación de infracciones en el SIMIT; y, si el vehículo tiene gravamen "
            + "activo, el levantamiento o la autorización del beneficiario. El Organismo de Tránsito "
            + "realiza estas validaciones al momento del trámite; este documento no las certifica ni "
            + "las reemplaza.";

        var declaraciones =
            "EL TRANSFERENTE declara bajo la gravedad del juramento: (i) que es el legítimo "
            + "propietario del vehículo; (ii) que el vehículo no se encuentra sujeto a medidas que "
            + "impidan el traspaso, distintas de las expresamente informadas; y (iii) que la "
            + "información registrada en el RNA es fiel reflejo de la situación del vehículo.";

        var notaDeFirmas =
            "Rige íntegramente el art. 5.3.2.1 de la Resolución 20233040017145 de 2023. No aplican "
            + "las exenciones del art. 5.3.2.2: el adquirente no es el locatario del contrato de "
            + "leasing.";

        var transferencia = TransferClausulas.TransferenciaDeDominio(negocio);
        var contraprestacion = TransferClausulas.Contraprestacion(negocio);
        var gravamen = TransferClausulas.Gravamen(model.Gravamen);
        var asuncion = TransferClausulas.Asuncion(negocio);
        var remolque = TransferClausulas.Remolque(model.Vehiculo.ClaseVehiculo);

        // VB-C-07 sobre el texto REAL que se va a componer, no sobre una plantilla teórica.
        VerificarSinExenciones(
        [
            comparecientes, regimen, transferencia, contraprestacion, gravamen, declaraciones,
            requisitosPlenos, TransferClausulas.CargasFiscales, asuncion, remolque, notaDeFirmas,
        ]);

        TransferDocumentBlocks.Encabezado(col, model);

        TransferDocumentBlocks.Clausula(
            col,
            "COMPARECIENTES",
            comparecientes,
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
            "SEGUNDA — ANTECEDENTE DE DOMINIO Y RÉGIMEN APLICABLE",
            regimen);

        TransferDocumentBlocks.Clausula(
            col,
            "TERCERA — TRANSFERENCIA DE DOMINIO",
            transferencia,
            contraprestacion,
            gravamen);

        TransferDocumentBlocks.Clausula(
            col,
            "CUARTA — DECLARACIONES DEL TRANSFERENTE",
            declaraciones);

        TransferDocumentBlocks.Clausula(
            col,
            "QUINTA — REQUISITOS PLENOS DEL TRÁMITE",
            requisitosPlenos);

        TransferDocumentBlocks.Clausula(
            col,
            "SEXTA — RETENCIÓN EN LA FUENTE, IMPUESTOS Y DERECHOS DEL TRÁMITE",
            TransferClausulas.CargasFiscales,
            asuncion,
            remolque);

        TransferDocumentBlocks.Firmas(col, model.CiudadFirma, model.FechaFirma, model.Partes);
        TransferDocumentBlocks.NotaDeFirmas(col, notaDeFirmas);
        TransferDocumentBlocks.AdvertenciaNormativa(col);
    }

    /// <summary>
    /// VB-C-07 — ningún párrafo del escenario C invoca las exenciones del art. 5.3.2.2. Se ejecuta
    /// siempre, no solo en pruebas: si una edición futura pegara aquí la cláusula cuarta del
    /// escenario B, el generador falla antes de producir el PDF en lugar de emitir un documento que
    /// le prometa al tercero exenciones que no tiene.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si algún párrafo invoca una exención.</exception>
    public static void VerificarSinExenciones(IEnumerable<string> parrafos)
    {
        ArgumentNullException.ThrowIfNull(parrafos);

        foreach (var parrafo in parrafos.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            foreach (var marca in MarcasDeExencion)
            {
                if (parrafo.Contains(marca, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "VB-C-07: el documento del escenario C no puede invocar exenciones del "
                        + "art. 5.3.2.2 — el adquirente no es el locatario y no las hereda "
                        + $"(marca detectada: «{marca}»).");
                }
            }
        }
    }
}

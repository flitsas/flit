using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Standalone;

/// <summary>
/// Escenario B — transferencia unilateral de leasing al locatario, art. 5.3.2.2 (anexo §8.2 y §9.2).
///
/// <para><b>REGLA ESTRUCTURAL — el bloque de firma del adquirente/locatario NO SE INSTANCIA.</b>
/// Este escenario tiene <b>un</b> bloque de firma: el de la entidad financiera. El del locatario no
/// existe: no es un bloque vacío, ni oculto, ni suprimido en render, ni con visibilidad condicional
/// ni con ancho cero — <i>nunca entra al árbol del documento</i>. Aquí no hay ninguna decisión de
/// «mostrar u ocultar» porque no hay nada que decidir: a
/// <see cref="TransferDocumentBlocks.Firmas"/> se le pasa la lista de partes del modelo, que en
/// este escenario tiene exactamente un elemento (anexo §9.0.3, §9.2 y §10 regla #4).</para>
///
/// <para><b>Formulaciones prohibidas</b> (§9.2), todas equivalentes al defecto: columna del
/// adquirente con contenido vacío, celda de tabla en blanco, línea <c>______</c> sin rótulo, rótulo
/// <c>ADQUIRENTE</c> o <c>LOCATARIO</c> sin línea, bloque generado y luego condicionado a no
/// mostrarse, texto de «no requiere firma», espacio reservado por simetría visual. Un hueco de
/// firma, aun vacío, invita al Organismo de Tránsito o al mandatario a exigir una firma que las
/// excepciones 4.ª y 5.ª del art. 5.3.2.2 no requieren, y convierte una exención de la norma en una
/// observación de trámite.</para>
///
/// <para><b>Independencia del modo de firma.</b> La regla no depende de <c>{{modo_firma}}</c>: el
/// escenario decide cuántos bloques existen; el modo solo decide qué va dentro de un bloque que ya
/// existe (§9.0.3).</para>
///
/// <para><b>El locatario sí aparece en las cláusulas declarativas</b> —segunda y tercera—, que es
/// donde el anexo lo quiere. La verificación de §13.2 se acota al bloque de firmas y es
/// <b>textual</b>: extraído el texto del PDF, el bloque de firmas no contiene el rótulo
/// <c>ADQUIRENTE</c> ni el rótulo <c>LOCATARIO</c>.</para>
/// </summary>
internal static class TransferEscenarioB
{
    public static void Compose(ColumnDescriptor col, TransferDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var entidad = model.ParteConRol(TransferPartyRole.Transferente)
            ?? throw new InvalidOperationException("El escenario B exige la entidad financiera transferente.");
        var leasing = model.Leasing
            ?? throw new InvalidOperationException("El escenario B exige el antecedente de leasing.");

        if (model.Partes.Count != 1)
        {
            // Cinturón y tirantes de §9.2: si alguien añadiera una segunda parte al modelo del
            // escenario B, el bloque de firmas del locatario aparecería. Antes que emitir ese PDF,
            // el generador falla.
            throw new InvalidOperationException(
                "El escenario B comparece con una sola parte: la entidad financiera. El bloque de "
                + "firma del locatario no se instancia (art. 5.3.2.2; anexo §9.2 y §10 regla #4).");
        }

        if (model.Negocio is not null)
        {
            // §10 regla #3 / VB-B-05: el acto unilateral no declara precio ni contraprestación.
            throw new InvalidOperationException(
                "El escenario B no declara negocio ni precio entre las partes del instrumento "
                + "(art. 5.3.2.2, acto unilateral; VB-B-05).");
        }

        TransferDocumentBlocks.Encabezado(col, model);

        TransferDocumentBlocks.Clausula(
            col,
            "COMPARECIENTE",
            "Que el suscrito, " + TransferClausulas.Parte(entidad, "LA ENTIDAD FINANCIERA")
            + ", actuando en ejercicio de las facultades conferidas por el contrato de leasing "
            + "referenciado a continuación y en virtud del art. 5.3.2.2 de la Resolución "
            + "20233040017145 de 2023 del Ministerio de Transporte:");

        TransferDocumentBlocks.Clausula(
            col,
            "PRIMERA — IDENTIFICACIÓN DEL VEHÍCULO",
            "La entidad financiera declara que el vehículo objeto de la presente transferencia tiene "
            + "las siguientes características registradas en el RNA:");
        TransferDocumentBlocks.TablaVehiculo(col, model.Vehiculo);

        TransferDocumentBlocks.Clausula(
            col,
            "SEGUNDA — ANTECEDENTE LEASING Y FUNDAMENTO DE LA TRANSFERENCIA",
            Antecedente(leasing),
            Causal(leasing));

        TransferDocumentBlocks.Clausula(
            col,
            "TERCERA — TRANSFERENCIA UNILATERAL DE DOMINIO",
            "En virtud del art. 5.3.2.2 de la Resolución 20233040017145 de 2023, LA ENTIDAD FINANCIERA "
            + $"transfiere unilateralmente el pleno dominio del vehículo descrito en la cláusula "
            + $"primera al locatario {leasing.LocatarioNombre}, de conformidad con los términos del "
            + $"contrato de leasing No. {leasing.NoContrato}. Esta transferencia se realiza en "
            + "ejercicio de la facultad legal que asiste a la entidad financiera, sin que sea "
            + "necesaria la comparecencia, aceptación ni firma del destinatario en este documento ni "
            + "en el Formato Único de Solicitud de Trámite (Anexo 46).");

        TransferDocumentBlocks.Clausula(
            col,
            "CUARTA — EXENCIONES NORMATIVAS APLICABLES AL TRÁMITE (art. 5.3.2.2)",
            "De conformidad con el art. 5.3.2.2 de la Resolución 20233040017145 de 2023, en este "
            + "trámite no se exigen: (1.ª) revisión técnico-mecánica y de emisiones contaminantes; "
            + "(2.ª) presentación de imagen del código QR, certificación del "
            + "fabricante/ensamblador/importador ni improntas de guarismos de identificación; "
            + "(3.ª) validación de paz y salvo por concepto de multas por infracciones de tránsito; "
            + "(4.ª) presentación del locatario (Comprador) ante el Organismo de Tránsito; y "
            + "(5.ª) firma del Formato Único de Solicitud de Trámite (Anexo 46) por parte del "
            + "locatario (Comprador).",
            "Las exenciones anteriores son las establecidas literalmente por el art. 5.3.2.2 para el "
            + "trámite ante el Organismo de Tránsito. Este documento soporte privado se estructura "
            + "sin firma del destinatario porque el acto es unilateral: es una decisión de diseño del "
            + "instrumento, no una exención expresa de la norma sobre documentos privados.",
            // El hallazgo del dictamen (§6.3, VB-B-06): las cinco exenciones NO tocan lo fiscal.
            "El art. 5.3.2.2 no exime el numeral 5.º del art. 5.3.2.1: el Organismo de Tránsito "
            + "verifica el pago de la retención en la fuente, del impuesto sobre vehículos automotores "
            + "y de los derechos del trámite.");

        TransferDocumentBlocks.Clausula(
            col,
            "QUINTA — SOPORTES APORTADOS",
            Soportes(leasing));

        // UNA lista, UN bloque. `model.Partes` tiene exactamente un elemento y por eso el bloque del
        // destinatario no puede existir: no hay parámetro que dejar nulo ni columna que ocultar.
        TransferDocumentBlocks.Firmas(col, model.CiudadFirma, model.FechaFirma, model.Partes);

        TransferDocumentBlocks.NotaDeFirmas(
            col,
            "Actúa en virtud del art. 5.3.2.2 de la Resolución 20233040017145 de 2023. La "
            + "transferencia se realiza de forma unilateral; el destinatario no firma el Formato "
            + "Único de Solicitud de Trámite (art. 5.3.2.2, excepción 5.ª).");

        TransferDocumentBlocks.AdvertenciaNormativa(col);
    }

    private static string Antecedente(TransferDocumentLeasing leasing) =>
        $"El vehículo descrito fue entregado en leasing mediante contrato No. {leasing.NoContrato}, "
        + $"suscrito con {leasing.LocatarioNombre}, identificado(a) con "
        + $"{leasing.LocatarioIdentificacion}.";

    /// <summary>
    /// Causal de la transferencia (§8.2, cláusula segunda). La fecha solo se enuncia si se declaró:
    /// el checklist §13.1 prohíbe imprimir variables sin resolver.
    /// </summary>
    private static string Causal(TransferDocumentLeasing leasing)
    {
        var encabezado = leasing.FechaTerminacion is { } fecha
            ? $"Con fecha {TransferDocumentBlocks.FechaEnLetras(fecha)} se configuró la siguiente "
              + "causal de transferencia: "
            : "Se configuró la siguiente causal de transferencia: ";

        return encabezado + TransferPurchaseOption.Wording(leasing.TipoOpcionCompra);
    }

    /// <summary>
    /// Soportes del art. 5.3.2.2 y su Parágrafo 1.º: con opción automática basta el contrato de
    /// leasing; en los otros dos casos se aporta además la declaración de terminación o de ejercicio
    /// de la opción de compra.
    /// </summary>
    private static string Soportes(TransferDocumentLeasing leasing)
    {
        var texto = "Se adjunta como soporte de esta transferencia la copia del contrato de leasing "
            + $"No. {leasing.NoContrato}";

        return TransferPurchaseOption.RequiereDeclaracionAdicional(leasing.TipoOpcionCompra)
            ? texto + ", junto con la declaración de terminación del contrato o de ejercicio de la "
                + "opción de compra."
            : texto + ". Tratándose de una opción de compra automática, el art. 5.3.2.2, Parágrafo "
                + "1.º, no exige declaración adicional.";
    }
}

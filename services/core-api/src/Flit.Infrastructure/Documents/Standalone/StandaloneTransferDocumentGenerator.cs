using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Infrastructure.Documents.Branding;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Standalone;

/// <summary>
/// Adaptador del puerto <see cref="IStandaloneTransferGenerator"/> (Feature #12201, I2): produce el
/// PDF del Documento de Transferencia de Dominio con QuestPDF y el membrete institucional FLIT, el
/// mismo camino de los demás documentos generados del repositorio.
///
/// <para><b>Los tres escenarios del anexo están registrados</b> en el <c>switch</c> de
/// <see cref="Compose"/>: A (§8.1), B (§8.2) y C (§8.3). Cada uno decide qué cláusulas existen y
/// <b>cuántas partes comparecen</b>, y con eso cuántos bloques de firma se instancian: el generador
/// no compone un documento genérico al que luego se le apagan piezas (§9.0.3).</para>
///
/// <para><b>No depende de ningún lector de firmas.</b> No se inyecta <c>ISignatureVaultReader</c> y
/// no se usa <c>FlitFirmaBlock</c>: el modo de firma es <c>MANUSCRITA</c> fijo (anexo §9.0) y el PDF
/// no puede llevar leyenda de firma electrónica ni sello, aunque la parte tenga firma custodiada
/// vigente en el baúl.</para>
/// </summary>
internal sealed class StandaloneTransferDocumentGenerator : IStandaloneTransferGenerator
{
    static StandaloneTransferDocumentGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public RenderedStandaloneDocument Render(TransferDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.SignatureMode != TransferSignatureMode.Manuscrita)
        {
            // El modo ESTAMPADA está DIFERIDO (anexo §9.4) y exige ADR previo sobre la evidencia de
            // consentimiento. Fallar aquí es preferible a emitir un documento con un modo que nadie
            // dictaminó.
            throw new NotSupportedException(
                $"Modo de firma no soportado: solo {TransferSignatureMode.Manuscrita} está vigente.");
        }

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                FlitLetterhead.ApplyTo(page);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily(FlitDocumentTheme.FontRegular));

                FlitLetterhead.Content(page).Column(col =>
                {
                    col.Spacing(2);
                    Compose(col, model);
                });
            });
        }).GeneratePdf();

        var filename = $"transferencia_dominio_{model.Vehiculo.Placa}_{model.ReferenceNumber}.pdf";

        return new RenderedStandaloneDocument(filename, "application/pdf", bytes);
    }

    private static void Compose(ColumnDescriptor col, TransferDocumentModel model)
    {
        switch (model.Scenario)
        {
            case TransferScenario.TraspasoOrdinario:
                TransferEscenarioA.Compose(col, model);
                break;

            case TransferScenario.UnilateralLeasing:
                TransferEscenarioB.Compose(col, model);
                break;

            case TransferScenario.FinancieraATercero:
                TransferEscenarioC.Compose(col, model);
                break;

            default:
                // Un escenario nuevo del anexo debe fallar aquí de forma explícita: el handler ya lo
                // habría rechazado antes, así que llegar hasta acá es un contrato roto, no una opción.
                throw new NotSupportedException(
                    $"Escenario de transferencia no implementado: {model.Scenario}.");
        }
    }
}

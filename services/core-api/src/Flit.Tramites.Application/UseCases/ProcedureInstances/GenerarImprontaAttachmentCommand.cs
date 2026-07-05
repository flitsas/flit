using Flit.Modules.Improntas.Domain;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Resultado de generar + adjuntar una impronta desde un trámite.</summary>
public sealed record GenerarImprontaAttachmentResult(
    Guid AttachmentId, string Filename, string Sha256, string Radicado, string Hash);

/// <summary>
/// Genera el Certificado de Improntas Digitales (Kyverum RUNT, mismo proveedor que el módulo admin
/// <c>/admin/improntas</c>) con los datos reales del trámite y lo adjunta como documento tipo
/// <c>impronta</c>, reusando <see cref="UploadAttachmentHandler"/> para garantizar idéntico
/// storage/auto-marcado de checklist que una subida manual.
///
/// Datos tomados del trámite: placa (<c>field_values.plate</c>) o, si aún no hay placa asignada
/// (matrícula inicial), el VIN (<c>field_values.vin</c>); documento del propietario (actor
/// <c>comprador</c> en matrícula inicial, SIEMPRE <c>vendedor</c> en traspaso — dueño actual al
/// radicar); nombre de organización = organismo de tránsito ya seleccionado en el paso FUR
/// (<c>field_values.transit_office_name</c>); operador = usuario autenticado que solicita la
/// generación, resuelto por <c>Users.DisplayName</c>.
///
/// A diferencia del FUR (que reemplaza en cada regeneración), este handler es idempotente por
/// NO-regeneración: si ya existe un adjunto tipo <c>impronta</c> (subido a mano o generado antes),
/// no vuelve a invocar a Kyverum.
/// </summary>
public sealed class GenerarImprontaAttachmentHandler(
    IProcedureInstanceRepository repo,
    IImprontaExternalClient externalClient,
    UploadAttachmentHandler uploadHandler)
{
    public async Task<(GenerarImprontaAttachmentResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid requestedBy,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithFurGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");
        if (instance.Status != TramiteEstado.Borrador)
            return (null, "not_draft");
        if (instance.Attachments.Any(a => string.Equals(a.Tipo, "impronta", StringComparison.OrdinalIgnoreCase)))
            return (null, "impronta_ya_existe");

        var fv = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        var transitOfficeName = Get(fv, "transit_office_name");
        if (string.IsNullOrWhiteSpace(Get(fv, "transit_office_code")) || string.IsNullOrWhiteSpace(transitOfficeName))
            return (null, "organismo_requerido");

        var placa = Get(fv, "plate");
        var vin = Get(fv, "vin");
        if (string.IsNullOrWhiteSpace(placa) && string.IsNullOrWhiteSpace(vin))
            return (null, "identificador_vehiculo_requerido");

        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var esTraspaso = string.Equals(codigo, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase);
        // Traspaso: SIEMPRE el vendedor (dueño actual al radicar), nunca el comprador.
        var rolPropietario = esTraspaso ? "vendedor" : "comprador";
        var propietario = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, rolPropietario, StringComparison.OrdinalIgnoreCase));
        if (propietario is null || string.IsNullOrWhiteSpace(propietario.DocumentNumber))
            return (null, "documento_propietario_requerido");

        var operador = await repo.GetUserDisplayNameAsync(requestedBy, ct);
        if (string.IsNullOrWhiteSpace(operador))
            return (null, "operador_no_resuelto");

        ImprontaExternalResult providerResult;
        try
        {
            providerResult = await externalClient.GenerarAsync(
                new ImprontaExternalRequest(
                    // Matrícula inicial (sin placa asignada aún): NO se envía el campo placa (ni
                    // siquiera vacío) — solo Vin. Traspaso: se envía la placa normalmente.
                    Placa: string.IsNullOrWhiteSpace(placa) ? null : placa,
                    Documento: propietario.DocumentNumber.Trim(),
                    NumMotor: null,
                    NumChasis: null,
                    NumSerie: null,
                    Marca: null,
                    Linea: null,
                    Modelo: null,
                    OrgNombre: transitOfficeName!.Trim(),
                    OrgNit: null,
                    OrgCiudad: null,
                    Operador: operador.Trim(),
                    // VIN solo cuando aún no hay placa asignada (matrícula inicial).
                    Vin: string.IsNullOrWhiteSpace(placa) ? vin : null),
                ct).ConfigureAwait(false);
        }
        catch (ImprontaRuntException ex)
        {
            return (null, MapProviderError(ex));
        }

        var pdfBytes = ImprontaPdfDecoder.Decode(providerResult.PdfDataUri);
        var safeRef = instance.ReferenceNumber.Replace('/', '-');
        var filename = $"impronta_{safeRef}.pdf";

        var (uploaded, uploadError) = await uploadHandler.HandleAsync(
            id,
            tenantId,
            new UploadAttachmentInput("impronta", filename, "application/pdf", pdfBytes.Length, new MemoryStream(pdfBytes)),
            requestedBy,
            ct);
        if (uploaded is null)
            return (null, uploadError);

        return (new GenerarImprontaAttachmentResult(
            uploaded.Id, uploaded.Filename, uploaded.Sha256, providerResult.Radicado, providerResult.Hash), null);
    }

    private static string? Get(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) ? v : null;

    private static string MapProviderError(ImprontaRuntException ex) =>
        ImprontaProviderErrorClassifier.Classify(ex) switch
        {
            ImprontaProviderErrorKind.Unavailable => "provider_unavailable",
            ImprontaProviderErrorKind.Validation => "provider_validation",
            _ => "provider_unauthorized",
        };
}

using Flit.Modules.Improntas.Domain;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;

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
/// Datos tomados del trámite: en matrícula inicial SIEMPRE se identifica el vehículo por VIN
/// (<c>field_values.vin</c>), incluso si el RUNT ya devolvió una placa asignada durante la consulta
/// — el radicador solo tiene certeza del VIN grabado en el vehículo, la placa es un dato que solo
/// conoce el organismo de tránsito y puede no coincidir con el documento del comprador (verificado
/// contra el proveedor real: enviar placa+documento sin relación devuelve <c>ok:false</c>, enviar
/// vin+documento sí resuelve). En traspaso (vehículo ya matriculado) se usa la placa
/// (<c>field_values.plate</c>). Documento del propietario: actor <c>comprador</c> en matrícula
/// inicial, SIEMPRE <c>vendedor</c> en traspaso — dueño actual al radicar; nombre de organización =
/// organismo de tránsito ya seleccionado en el paso FUR (<c>field_values.transit_office_name</c>);
/// operador = usuario autenticado que solicita la generación, resuelto por <c>Users.DisplayName</c>.
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
        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
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

        // ADR-0051 — la identificación del vehículo y el propietario las decide el perfil de
        // capacidades del tipo, no una igualdad exacta con TRASPASO_STANDARD: así TRASPASO_UNILATERAL
        // (entryMode PLATE, requiresSeller true) queda cubierto con el mismo criterio, sin un `if`
        // nuevo por tipo.
        var profile = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        // Matrícula inicial (entryMode VIN): SIEMPRE por VIN (el radicador no conoce la placa aunque
        // el RUNT ya tenga una asignada). Todo lo demás (PLATE/BOTH/ausente — vehículo ya matriculado,
        // mismo default que OperatorChoosesTransitOffice): SIEMPRE por placa.
        var usaPlaca = !string.Equals(
            profile.EntryMode, ProcedureTypeGateProfile.EntryModeVin, StringComparison.OrdinalIgnoreCase);

        if (usaPlaca ? string.IsNullOrWhiteSpace(placa) : string.IsNullOrWhiteSpace(vin))
            return (null, "identificador_vehiculo_requerido");
        // Con parte vendedora (RequiresSeller): SIEMPRE el vendedor (dueño actual al radicar), nunca
        // el comprador — cierto tanto en TRASPASO_STANDARD como en TRASPASO_UNILATERAL.
        var rolPropietario = profile.RequiresSeller ? "vendedor" : "comprador";
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
                    // entryMode != VIN (vehículo ya matriculado): se envía la placa. entryMode VIN
                    // (matrícula inicial): NUNCA se envía placa (ni siquiera si el RUNT ya devolvió una
                    // en la consulta) — ver nota de clase sobre por qué se prioriza siempre el VIN.
                    Placa: usaPlaca ? placa : null,
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
                    // Matrícula inicial (entryMode VIN): siempre VIN. Resto: no se envía (se
                    // identifica por placa).
                    Vin: usaPlaca ? null : vin),
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

/// <summary>
/// Adaptador de <see cref="IImprontaAutoGenerator"/> sobre <see cref="GenerarImprontaAttachmentHandler"/>
/// (HU #11017): permite que el consolidado genere la impronta que falte sin acoplarse a la firma del
/// handler. Best-effort por contrato — devuelve el código de error en vez de lanzar.
/// </summary>
public sealed class ImprontaAutoGenerator(GenerarImprontaAttachmentHandler handler) : IImprontaAutoGenerator
{
    public async Task<string?> TryGenerateAsync(
        Guid id, Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var (_, error) = await handler.HandleAsync(id, tenantId, userId, ct).ConfigureAwait(false);
        return error;
    }
}

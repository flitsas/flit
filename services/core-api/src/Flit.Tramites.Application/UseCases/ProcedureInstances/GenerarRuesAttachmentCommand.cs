using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Resultado de autogenerar + adjuntar el Certificado RUES desde un trámite.</summary>
public sealed record GenerarRuesAttachmentResult(Guid AttachmentId, string Filename, string Sha256, string? Radicado);

/// <summary>
/// Autogenera el Certificado RUES (RF36) para un actor jurídico (NIT) reusando
/// <see cref="UploadAttachmentHandler"/> — idéntico storage/auto-marcado de checklist que una subida
/// manual — y lo adjunta como documento tipo <c>rues</c>.
///
/// Aditivo y con respaldo: el cliente externo (<see cref="IRuesExternalClient"/>) es OPCIONAL. Si la
/// integración no está habilitada (no hay cliente registrado; ver <c>Rues:Enabled</c> en
/// Infraestructura), el handler responde <c>rues_autogen_disabled</c> y el operador sube el RUES a mano
/// (el tipo de documento y su regla condicional <c>nit_rues</c> ya existen). Idempotente por
/// NO-regeneración: si ya existe un adjunto tipo <c>rues</c>, no vuelve a invocar al proveedor.
/// </summary>
public sealed class GenerarRuesAttachmentHandler(
    IProcedureInstanceRepository repo,
    UploadAttachmentHandler uploadHandler,
    IRuesExternalClient? externalClient = null)
{
    private const string NitDocumentType = "NIT";

    public async Task<(GenerarRuesAttachmentResult? Result, string? Error)> HandleAsync(
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
        if (instance.Attachments.Any(a => string.Equals(a.Tipo, "rues", StringComparison.OrdinalIgnoreCase)))
            return (null, "rues_ya_existe");

        // El RUES solo aplica a actores jurídicos (NIT). Tomamos el primer actor con documento NIT.
        var actorNit = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.DocumentType, NitDocumentType, StringComparison.OrdinalIgnoreCase));
        if (actorNit is null || string.IsNullOrWhiteSpace(actorNit.DocumentNumber))
            return (null, "actor_nit_requerido");

        // Sin cliente registrado ⇒ autogeneración deshabilitada ⇒ respaldo: carga manual del RUES.
        if (externalClient is null)
            return (null, "rues_autogen_disabled");

        RuesExternalResult providerResult;
        try
        {
            providerResult = await externalClient
                .GenerarAsync(new RuesExternalRequest(actorNit.DocumentNumber.Trim(), actorNit.FullName), ct)
                .ConfigureAwait(false);
        }
        catch (RuesExternalException ex)
        {
            return (null, ex.IsTransient ? "provider_unavailable" : "provider_error");
        }

        var pdfBytes = DecodePdf(providerResult.PdfDataUri);
        if (pdfBytes.Length == 0)
            return (null, "provider_error");

        var safeRef = instance.ReferenceNumber.Replace('/', '-');
        var filename = $"rues_{safeRef}.pdf";

        var (uploaded, uploadError) = await uploadHandler.HandleAsync(
            id,
            tenantId,
            new UploadAttachmentInput("rues", filename, "application/pdf", pdfBytes.Length, new MemoryStream(pdfBytes)),
            requestedBy,
            ct);
        if (uploaded is null)
            return (null, uploadError);

        return (new GenerarRuesAttachmentResult(uploaded.Id, uploaded.Filename, uploaded.Sha256, providerResult.Radicado), null);
    }

    /// <summary>Decodifica un PDF entregado como Data URI (<c>data:...;base64,</c>) o base64 crudo.</summary>
    private static byte[] DecodePdf(string pdfDataUri)
    {
        if (string.IsNullOrWhiteSpace(pdfDataUri))
            return [];
        var payload = pdfDataUri;
        var comma = pdfDataUri.IndexOf(',');
        if (pdfDataUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
            payload = pdfDataUri[(comma + 1)..];
        try
        {
            return Convert.FromBase64String(payload.Trim());
        }
        catch (FormatException)
        {
            return [];
        }
    }
}

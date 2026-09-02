using System.Text.Json.Nodes;
using System.Text.Json;
using Flit.Tramites.Application.Ocr;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using System.Text;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Adjunta la Licencia de Tránsito (LT) que el Organismo de Tránsito emite al decidir el
/// trámite. A diferencia de los adjuntos del operador (solo en <c>borrador</c>), la LT se
/// adjunta con el trámite <c>entregado</c> o <c>aprobado</c>. Idempotente: re-adjuntar
/// reemplaza la LT previa (el consolidado siempre toma la vigente). Registra el evento
/// <c>lt_adjuntada</c> en la bitácora.
/// <para><b>HU #11996 — verificación por OCR.</b> La LT que entrega el OT es el resultado del trámite:
/// si viene equivocada, el expediente queda cerrado con el documento de otro vehículo y nadie lo nota.
/// Se analiza aquí, en el backend, y no en la modal, porque hay DOS caminos que llegan a este handler
/// —el cargue junto con la aprobación y la acción dedicada de reintento— y ponerlo en el front
/// obligaría a recordarlo en ambos.</para>
/// <para>El análisis <b>nunca</b> bloquea el adjunto, igual que en el wizard: la LT es el entregable
/// del OT y perderla por un fallo del proveedor de IA sería peor que no verificarla. El resultado
/// viaja en la respuesta para que la pantalla lo muestre, y queda en el evento <c>lt_adjuntada</c>
/// para que sea auditable después.</para>
/// </summary>
public sealed class AdjuntarLicenciaTransitoHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage,
    AnalyzeDocumentHandler? ocr = null)
{
    /// <summary>
    /// Prompt con el que se verifica la LT. Es el mismo documento que la casilla del wizard, así que
    /// comparten prompt aunque el tipo documental difiera (<c>licencia_transito</c> lo emite el OT;
    /// <c>tarjeta_propiedad</c> es la licencia vigente que el gestor aporta como insumo).
    /// </summary>
    public const string TipoOcr = "tarjeta_propiedad";
    /// <summary>Tipo documental de la Licencia de Tránsito en <c>procedure_instance_attachments</c>.</summary>
    public const string Tipo = "licencia_transito";

    /// <param name="ocrPrecomputado">
    /// HU #12042 — resultado que el frontend YA obtuvo al seleccionar el archivo, para mostrárselo al OT
    /// ANTES de que decida. Si viene, NO se vuelve a analizar: además de no pagar dos veces, garantiza
    /// que lo que queda registrado sea exactamente lo que el usuario vio. No es un detalle: en la prueba
    /// en vivo dos análisis del mismo archivo devolvieron VIN distintos, y registrar uno mientras se
    /// muestra el otro sería peor que no mostrar nada.
    /// </param>
    public async Task<(AttachmentDto? Result, string? Error, DocumentOcrResponse? Ocr)> HandleAsync(
        Guid id,
        Guid tenantId,
        UploadAttachmentInput input,
        Guid? uploadedBy = null,
        JsonObject? ocrPrecomputado = null,
        CancellationToken ct = default)
    {
        if (input.Content is null || input.SizeBytes <= 0)
            return (null, "missing_file", null);
        if (string.IsNullOrWhiteSpace(input.Mimetype) || !AttachmentRules.ValidMimetypes.Contains(input.Mimetype))
            return (null, "invalid_mime", null);
        if (input.SizeBytes > AttachmentRules.MaxSizeBytes)
            return (null, "file_too_large", null);

        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found", null);

        // La LT la emite el OT sobre un trámite ya radicado: entregado (en decisión) o aprobado.
        if (instance.Status is not (TramiteEstado.Entregado or TramiteEstado.Aprobado))
            return (null, "estado_invalido", null);

        // El stream se consume una sola vez y el OCR necesita los mismos bytes que se guardan,
        // así que se materializa antes de tocar el almacenamiento.
        var bytes = await LeerTodoAsync(input.Content, ct).ConfigureAwait(false);
        var ocrResultado = ocrPrecomputado is not null
            ? new DocumentOcrResponse(true, TipoOcr, ocrPrecomputado)
            : await AnalizarSinBloquearAsync(bytes, ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        // Reemplazo de la LT previa (misma semántica que el consolidado re-generado).
        foreach (var prev in instance.Attachments
                     .Where(a => string.Equals(a.Tipo, Tipo, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            storage.Delete(prev.StoragePath);
            instance.Attachments.Remove(prev);
            repo.RemoveAttachment(prev);
        }

        var filename = string.IsNullOrWhiteSpace(input.Filename) ? "licencia_transito.pdf" : input.Filename.Trim();
        using var contenido = new MemoryStream(bytes, writable: false);
        var stored = await storage.SaveAsync(id, Tipo, filename, contenido, ct);

        // Guarda FK (HU #10431): si el sub del JWT no existe en identity.users, se registra null.
        var resolvedUploadedBy = uploadedBy is { } ub && ub != Guid.Empty
            && await repo.UserExistsAsync(ub, ct).ConfigureAwait(false)
            ? uploadedBy
            : null;

        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = Tipo,
            Filename = filename,
            Mimetype = input.Mimetype.Trim().ToLowerInvariant(),
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            StoragePath = stored.StoragePath,
            Source = "ot",
            UploadedAt = now,
            UploadedBy = resolvedUploadedBy,
        };
        instance.Attachments.Add(attachment);
        repo.Add(attachment);

        // Feature #10701 / HU #10860 — adjuntar la LT cambia el contenido del expediente: invalida
        // los consolidados persistidos (maestro y wizard) para que la próxima generación la incluya.
        instance.InvalidarConsolidados();

        var evento = new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = "lt_adjuntada",
            Payload = JsonSerializer.Serialize(new
            {
                filename,
                sha256 = stored.Sha256,
                adjuntada_at = now,
                // Deja rastro de la verificación: sin esto, un expediente cerrado con una LT
                // equivocada no tendría cómo explicarse después.
                ocr_verificada = ocrResultado?.Data is not null
                    ? ocrResultado.Data["es_valido"]?.GetValue<bool>()
                    : null,
                ocr_placa = ocrResultado?.Data?["vehiculo_placa"]?.GetValue<string>(),
                ocr_vin = ocrResultado?.Data?["vehiculo_vin"]?.GetValue<string>(),
                // HU #12043 — deja constancia de si la licencia era de ESTE vehículo. Sin esto un
                // expediente podía cerrarse con `ocr_verificada: true` y la licencia de otro carro:
                // el documento era una licencia legítima, solo que no la del trámite.
                ocr_vin_coincide = Coincide(ocrResultado?.Data?["vehiculo_vin"]?.GetValue<string>(), instance.Vin),
                ocr_placa_coincide = Coincide(ocrResultado?.Data?["vehiculo_placa"]?.GetValue<string>(), instance.Plate),
            }),
            CreatedAt = now,
            CreatedBy = resolvedUploadedBy,
        };
        instance.Events.Add(evento);
        repo.Add(evento);

        await repo.SaveChangesAsync(ct);

        return (UploadAttachmentHandler.ToDto(attachment), null, ocrResultado);
    }

    /// <summary>
    /// Analiza la LT sin dejar que ningún fallo impida adjuntarla. Devuelve null cuando el análisis
    /// no se pudo hacer —proveedor caído, sin API key, archivo mayor de 10 MB o formato no
    /// soportado—, que la pantalla presenta como «no analizado», no como rechazo.
    /// </summary>
    /// <summary>
    /// Compara un identificador leído por el OCR contra el del trámite, ignorando espacios, guiones
    /// y mayúsculas. Devuelve <c>null</c> cuando falta cualquiera de los dos lados: sin las dos
    /// mitades no hay nada que afirmar, y un <c>false</c> ahí sería una acusación inventada.
    /// </summary>
    private static bool? Coincide(string? leido, string? esperado)
    {
        var a = Normalizar(leido);
        var b = Normalizar(esperado);
        if (a.Length == 0 || b.Length == 0) return null;
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    private static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
        var sb = new StringBuilder(valor.Length);
        foreach (var c in valor)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    private async Task<DocumentOcrResponse?> AnalizarSinBloquearAsync(byte[] bytes, CancellationToken ct)
    {
        if (ocr is null)
            return null;
        try
        {
            var (resultado, _) = await ocr.HandleAsync(TipoOcr, bytes, ct).ConfigureAwait(false);
            return resultado;
        }
        catch
        {
            // Silencio deliberado: el adjunto es el entregable y el análisis es una ayuda.
            return null;
        }
    }

    private static async Task<byte[]> LeerTodoAsync(Stream origen, CancellationToken ct)
    {
        if (origen is MemoryStream ya)
            return ya.ToArray();
        using var buffer = new MemoryStream();
        await origen.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }
}

/// <summary>
/// Resuelve el PDF del expediente consolidado más reciente del trámite para descarga
/// (lo consume el perfil OT para VISUALIZAR el consolidado sin regenerarlo). Considera tanto
/// el consolidado del wizard (<c>consolidado</c>) como el consolidado maestro
/// (<c>consolidado_maestro</c>, Feature #10701) y devuelve el más reciente entre ambos: así
/// "Ver consolidado" muestra el maestro cuando es lo único que el OT generó.
/// Errores: <c>not_found</c> (instancia), <c>consolidado_no_generado</c>, <c>file_missing</c>.
/// </summary>
public sealed class DescargarConsolidadoHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage)
{
    private static readonly HashSet<string> ConsolidadoTipos = new(StringComparer.OrdinalIgnoreCase)
    {
        "consolidado",
        "consolidado_maestro",
    };

    public async Task<(AttachmentDownload? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var consolidado = instance.Attachments
            .Where(a => ConsolidadoTipos.Contains(a.Tipo))
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefault();
        if (consolidado is null)
            return (null, "consolidado_no_generado");

        var stream = await storage.OpenReadAsync(consolidado.StoragePath, ct);
        if (stream is null)
            return (null, "file_missing");

        var filename = string.IsNullOrWhiteSpace(consolidado.Filename) ? "consolidado.pdf" : consolidado.Filename;
        var mime = string.IsNullOrWhiteSpace(consolidado.Mimetype) ? "application/pdf" : consolidado.Mimetype;
        return (new AttachmentDownload(stream, mime, filename), null);
    }
}

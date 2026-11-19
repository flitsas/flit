using System.Text.Json;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Tipo de documento que el admin puede cargar a un trámite (habilitado para su tipo de trámite).</summary>
public sealed record AdminDocumentoTipoDto(string Tipo, string Nombre);

/// <summary>Archivo y datos de la carga administrativa de un documento (Bug #14604).</summary>
public sealed record AdminCargarDocumentoInput(
    string Tipo,
    string? Observacion,
    string Filename,
    string Mimetype,
    long SizeBytes,
    Stream Content);

/// <summary>Resultado de la carga: el adjunto nuevo y los que quedaron como versión anterior (históricos).</summary>
public sealed record AdminCargarDocumentoResult(
    Guid AttachmentId,
    string Tipo,
    string Filename,
    IReadOnlyList<Guid> PreviousAttachmentIds);

/// <summary>
/// Bug #14604 — «Tipos de documento que el admin puede cargar»: los de la matriz documental del tipo de trámite de la
/// instancia, ya con las reglas condicionales aplicadas. Es la MISMA fuente que el checklist del wizard
/// (<see cref="GetChecklistHandler"/>): matriz viva del gestor, sin los documentos que genera el sistema. No depende
/// del estado del trámite.
/// </summary>
public sealed class AdminDocumentoTiposHandler(GetChecklistHandler checklist)
{
    public async Task<(IReadOnlyList<AdminDocumentoTipoDto>? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var (response, error) = await checklist.HandleAsync(id, tenantId, ct).ConfigureAwait(false);
        if (error is not null || response is null)
            return (null, error ?? "not_found");

        var tipos = response.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.DocTipo))
            .GroupBy(i => i.DocTipo!.Trim().ToLowerInvariant())
            .Select(g => new AdminDocumentoTipoDto(
                g.Key,
                string.IsNullOrWhiteSpace(g.First().Label) ? g.Key : g.First().Label))
            .ToList();
        return (tipos, null);
    }
}

/// <summary>
/// Bug #14604 — «Cargar documento» del admin FLIT sobre un trámite en CUALQUIER estado (incluidos aprobado, anulado y
/// revocado). Acción administrativa con permiso propio (<c>AdminTramiteCargarDocumento</c>): NO reutiliza ni relaja
/// <see cref="AttachmentRules.AllowsUploadInState"/>, que sigue siendo el gate del gestor.
///
/// <para>Reglas:
/// (1) solo tipos habilitados para el tipo de trámite (<see cref="AdminDocumentoTiposHandler"/>);
/// (2) observación obligatoria;
/// (3) el adjunto vigente anterior del mismo tipo NO se borra: queda <see cref="ProcedureInstanceAttachment.IsHistorico"/>
/// y sigue visible y descargable como «versión anterior». A diferencia de la subida del gestor, aquí aplica a TODOS los
/// tipos, incluidos los «bolsa» (<see cref="AttachmentRules.TiposMultiples"/>, p. ej. «Otros documentos»): la acción se
/// llama «Actualizar documento» y el modal le promete al admin «El documento actual quedará como versión anterior» sin
/// distinguir tipo (Bug #14604, novedad QA 2026-11-19). El histórico queda fuera del consolidado (<c>Efectivos()</c>);
/// no choca con el Bug #14570 («Otro» bolsa visible), que solo gobierna la subida del gestor (añade sin retirar nada).
/// Lo que generó el sistema o personalizó la compañía no se retira;
/// (4) «gana quien carga primero» (HU #13265): si FLITO ya cargó ese tipo, se responde 409 igual que al gestor;
/// (5) trazabilidad: evento propio <c>documento_cargado_admin</c> en la bitácora, con el estado del trámite al cargar.</para>
///
/// <para>El consolidado no se regenera aquí: al guardar el adjunto, <c>ConsolidadoVigenciaTracker</c> baja las marcas de
/// vigencia (en cualquier estado) y la próxima entrega lo reconstruye (en estado final, por obsolescencia —
/// <c>EntregarConsolidadoHandler</c>). Un consolidado cargado a mano (<c>Source="user"</c>) prevalece, como siempre.</para>
/// </summary>
public sealed class AdminCargarDocumentoHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage,
    AdminDocumentoTiposHandler tiposHabilitados,
    AttachmentValidator? validator = null)
{
    /// <summary>Tipo del evento PROPIO de bitácora.</summary>
    public const string EventoTipo = "documento_cargado_admin";

    public const string ObservacionRequerida = "observacion_requerida";
    public const string ObservacionDemasiadoLarga = "observacion_demasiado_larga";
    public const string TipoNoHabilitado = "tipo_no_habilitado";

    /// <summary>Máximo de la observación (caracteres, ya recortada).</summary>
    public const int MaxObservacion = 1000;

    public async Task<(AdminCargarDocumentoResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        AdminCargarDocumentoInput input,
        Guid? userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Content is null || input.SizeBytes <= 0)
            return (null, "missing_file");

        var observacion = input.Observacion?.Trim();
        if (string.IsNullOrEmpty(observacion))
            return (null, ObservacionRequerida);
        if (observacion.Length > MaxObservacion)
            return (null, ObservacionDemasiadoLarga);

        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, "not_found");

        var validationError = validator is not null
            ? await validator.ValidateAsync(input.Tipo, input.Mimetype, input.SizeBytes, ct).ConfigureAwait(false)
            : AttachmentRules.Validate(input.Tipo, input.Mimetype, input.SizeBytes);
        if (validationError is not null)
            return (null, validationError);

        var tipo = input.Tipo.Trim().ToLowerInvariant();

        var (habilitados, tiposError) = await tiposHabilitados.HandleAsync(id, tenantId, ct).ConfigureAwait(false);
        if (tiposError is not null || habilitados is null)
            return (null, tiposError ?? "not_found");
        if (!habilitados.Any(t => string.Equals(t.Tipo, tipo, StringComparison.OrdinalIgnoreCase)))
            return (null, TipoNoHabilitado);

        // HU #13265 — «gana quien carga primero»: el admin tampoco pisa lo que FLITO cargó.
        if (AttachmentRules.BloqueadoPorFlito(instance.Attachments, tipo))
            return (null, ExternalAttachmentRules.BlockedCode);

        // Los vigentes del mismo tipo pasan a histórico (no se borran). A diferencia de la subida del gestor, aquí
        // aplica también a los tipos «bolsa» (Bug #14604, novedad QA): «Actualizar documento» siempre reemplaza. Lo
        // generado por el sistema o personalizado por la compañía tiene su propio ciclo de vida y no se toca.
        var previos = instance.Attachments
            .Where(a => !a.IsHistorico
                        && string.Equals(a.Tipo, tipo, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(a.Source, "system", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(a.Source, "company", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Trazabilidad: lo que estaba en la casilla antes de la carga (el documento efectivo de ese tipo; en un
        // trámite revocado puede ser uno ya histórico), para que el historial diga qué archivo se reemplazó.
        var reemplazados = instance.Attachments
            .Efectivos()
            .Where(a => string.Equals(a.Tipo, tipo, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var filename = string.IsNullOrWhiteSpace(input.Filename) ? "file" : input.Filename.Trim();
        var stored = await storage.SaveAsync(id, tipo, filename, input.Content, ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        foreach (var prev in previos)
            prev.IsHistorico = true;

        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = tipo,
            Filename = filename,
            Mimetype = input.Mimetype.Trim().ToLowerInvariant(),
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            StoragePath = stored.StoragePath,
            Source = "user",
            UploadedAt = now,
            UploadedBy = userId,
        };
        instance.Attachments.Add(attachment);
        // PK store-generated con Id ya seteado: Added explícito para forzar INSERT (mismo patrón que la subida del gestor).
        repo.Add(attachment);

        ChecklistEstadoJson.AutoMark(instance, tipo);

        var previousIds = previos.Select(p => p.Id).ToList();
        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = EventoTipo,
            Payload = JsonSerializer.Serialize(new
            {
                tipo,
                filename,
                attachmentId = attachment.Id,
                previousAttachmentIds = previousIds,
                // Lo que se veía en la casilla antes de la carga: se abre desde la línea de tiempo.
                replacedAttachmentIds = reemplazados.Select(r => r.Id).ToList(),
                previousFilenames = reemplazados.Select(r => r.Filename).ToList(),
                estado = instance.Status,
                observacion,
            }),
            CreatedAt = now,
            CreatedBy = userId,
        }, ct).ConfigureAwait(false);

        if (!await UploadAttachmentHandler.SaveGanaElPrimeroAsync(repo, ct).ConfigureAwait(false))
        {
            // FLITO cargó ese tipo entre la lectura y el guardado: el binario recién subido no lo referencia nadie.
            storage.Delete(stored.StoragePath);
            return (null, ExternalAttachmentRules.BlockedCode);
        }

        return (new AdminCargarDocumentoResult(attachment.Id, tipo, filename, previousIds), null);
    }
}

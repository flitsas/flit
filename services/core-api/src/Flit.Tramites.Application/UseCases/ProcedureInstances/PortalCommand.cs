using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs públicos (portal del participante vía magic-link) ───────────────────

/// <summary>Resumen mínimo de la instancia para el portal (sin PII sensible interna).</summary>
public sealed record PortalInstanceSummary(
    string Referencia,
    string ModalidadEntrada,
    string? TipologiaCodigo,
    string? TipologiaNombre);

/// <summary>Estado de un documento requerido para el rol del participante.</summary>
public sealed record PortalDocumentoStatus(string Tipo, string Label, bool Obligatorio, bool Subido);

/// <summary>Paso de biométrica del participante (surface del flujo existente /biometric/{token}).</summary>
public sealed record PortalBiometricaStatus(bool Existe, string? Estado, bool Pendiente);

/// <summary>Paso de firma del participante.</summary>
public sealed record PortalFirmaStatus(bool Aplica, bool Existe, string? Estado, bool Firmada);

/// <summary>Pasos pendientes agregados para el rol del participante.</summary>
public sealed record PortalPasosPendientes(
    bool ConsentDado,
    IReadOnlyList<PortalDocumentoStatus> Documentos,
    PortalBiometricaStatus Biometrica,
    PortalFirmaStatus Firma,
    bool Completado);

/// <summary>Vista PÚBLICA del portal (no enumera PII: token inválido/usado/expirado → not_found).</summary>
public sealed record PortalViewDto(
    string Rol,
    string Nombre,
    bool ConsentDado,
    string ConsentVersion,
    string ConsentText,
    bool Expirado,
    bool Completado,
    PortalInstanceSummary Instancia,
    PortalPasosPendientes PasosPendientes);

/// <summary>Entrada de aceptación de consentimiento (IP/UA capturados por el endpoint).</summary>
public sealed record AceptarConsentimientoInput(string? Ip, string? UserAgent);

public sealed record AceptarConsentimientoResult(string ConsentVersion, DateTimeOffset Consent1581At);

/// <summary>Metadatos del documento subido vía portal (extraídos del IFormFile por el endpoint).</summary>
public sealed record SubirDocumentoPortalInput(
    string Tipo,
    string Filename,
    string Mimetype,
    long SizeBytes,
    Stream Content);

public sealed record FinalizarPortalResult(DateTimeOffset CompletedAt);

// ── Handler: vista del portal por token (público) ────────────────────────────

/// <summary>
/// Vista pública por token (magic-link). SEGURIDAD: no filtra existencia — token desconocido,
/// expirado o ya usado (completed_at) → <c>not_found</c> genérico (sin enumeración de PII). Agrega
/// los pasos pendientes del rol del participante: consentimiento, documentos (requeridos vs subidos),
/// biométrica (estado del flujo existente) y firma (estado de la firma de su rol).
/// </summary>
public sealed class GetPortalByTokenHandler(IProcedureInstanceRepository repo)
{
    public async Task<(PortalViewDto? Result, string? Error)> HandleAsync(
        string token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, "not_found");

        var participant = await repo.GetParticipantByTokenHashAsync(PortalToken.Hash(token), ct);
        if (participant is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        var expirado = participant.CompletedAt is null && now > participant.ExpiresAt;
        // Token usado o expirado: not_found genérico (no se filtra la causa para no enumerar PII).
        if (participant.CompletedAt is not null || expirado)
            return (null, "not_found");

        var instance = participant.ProcedureInstance!;
        var view = BuildView(participant, instance);
        return (view, null);
    }

    internal static PortalViewDto BuildView(ProcedureInstanceParticipant p, ProcedureInstance instance)
    {
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var tipologia = TramiteTipologiaCatalog.Get(codigo);

        var summary = new PortalInstanceSummary(
            instance.ReferenceNumber,
            instance.ModalidadEntrada,
            codigo,
            tipologia?.Nombre);

        var consentDado = p.Consent1581At is not null;

        // Documentos: ítems de checklist con DocTipo, marcados como subidos si hay un adjunto del tipo.
        var subidos = instance.Attachments
            .Select(a => a.Tipo)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var documentos = (tipologia?.Checklist ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.DocTipo))
            .Select(i => new PortalDocumentoStatus(
                i.DocTipo!, i.Label, i.Obligatorio, subidos.Contains(i.DocTipo!)))
            .ToList();

        // Biométrica del rol: surface del flujo existente. Si no existe, queda pendiente de creación
        // por el admin (NO se auto-crea aquí: requiere tipo_doc/documento que el portal no captura).
        var bio = FindForRol(instance.BiometricValidations, p.Rol, v => v.PartyRole);
        var biometrica = new PortalBiometricaStatus(
            Existe: bio is not null,
            Estado: bio?.Status,
            Pendiente: bio is null || bio.Status is not BiometricEstados.Aprobado);

        // Firma del rol: solo aplica a traspaso (comprador|vendedor). Mandatario no firma compraventa.
        var aplicaFirma = string.Equals(codigo, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase)
            && p.Rol is ParticipantRoles.Comprador or ParticipantRoles.Vendedor;
        var sig = aplicaFirma
            ? instance.Signatures.FirstOrDefault(s => string.Equals(s.Parte, p.Rol, StringComparison.OrdinalIgnoreCase))
            : null;
        var firma = new PortalFirmaStatus(
            Aplica: aplicaFirma,
            Existe: sig is not null,
            Estado: sig?.Estado,
            Firmada: sig?.Estado == SignatureEstados.Firmada);

        var pasos = new PortalPasosPendientes(
            consentDado, documentos, biometrica, firma, p.CompletedAt is not null);

        return new PortalViewDto(
            p.Rol, p.Nombre, consentDado,
            ParticipantRules.ConsentVersion, ParticipantRules.ConsentText,
            Expirado: false, Completado: p.CompletedAt is not null,
            summary, pasos);
    }

    /// <summary>
    /// Empareja la biométrica por Parte == rol. Tanto matrícula (única parte = comprador) como
    /// traspaso usan la parte explícita ("comprador"/"vendedor"); se mantiene un fallback tolerante
    /// a Parte vacía SOLO para comprador (datos legados anteriores a la unificación LV-1).
    /// </summary>
    private static ProcedureInstanceBiometricValidation? FindForRol(
        IEnumerable<ProcedureInstanceBiometricValidation> all, string rol, Func<ProcedureInstanceBiometricValidation, string?> parteOf)
    {
        var match = all.FirstOrDefault(v => string.Equals(parteOf(v), rol, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return match;
        // Tolerancia legado: validaciones de matrícula creadas con Parte vacía → comprador.
        return rol == ParticipantRoles.Comprador
            ? all.FirstOrDefault(v => string.IsNullOrEmpty(parteOf(v)))
            : null;
    }
}

// ── Handler: aceptar consentimiento (público) ────────────────────────────────

/// <summary>
/// Registra el consentimiento Ley 1581 del participante: versión vigente + IP + User-Agent (truncado)
/// como prueba de auditoría. SEGURIDAD: token inválido/expirado/usado → not_found genérico. Append de
/// evento <c>consentimiento_1581</c>. Idempotente: re-aceptar refresca la marca temporal y la prueba.
/// </summary>
public sealed class AceptarConsentimientoHandler(IProcedureInstanceRepository repo)
{
    public async Task<(AceptarConsentimientoResult? Result, string? Error)> HandleAsync(
        string token,
        AceptarConsentimientoInput input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, "not_found");

        var participant = await repo.GetParticipantByTokenHashAsync(PortalToken.Hash(token), ct);
        if (participant is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        if (participant.CompletedAt is not null || now > participant.ExpiresAt)
            return (null, "not_found");

        participant.Consent1581At = now;
        participant.ConsentVersion = ParticipantRules.ConsentVersion;
        participant.ConsentIp = Truncate(input.Ip, 64);
        participant.ConsentUserAgent = Truncate(input.UserAgent, ParticipantRules.UserAgentMaxLength);
        participant.UpdatedAt = now;

        await repo.AddEventAsync(EventFactory.Consentimiento1581(participant), ct);
        await repo.SaveChangesAsync(ct);

        return (new AceptarConsentimientoResult(ParticipantRules.ConsentVersion, now), null);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var v = value.Trim();
        return v.Length <= max ? v : v[..max];
    }
}

// ── Handler: subir documento (público) ───────────────────────────────────────

/// <summary>
/// Sube un documento del participante vía portal. SEGURIDAD: GATE de consentimiento — exige
/// <c>consent_1581_at</c> antes de cualquier escritura de datos (<c>consent_requerido</c>, 409);
/// token inválido/expirado/usado → not_found. Reusa <see cref="IAttachmentStorage"/> y la tabla de
/// adjuntos (mismo tipo/mime/size validados que el flujo autenticado). Append de evento
/// <c>documento_subido</c>. Solo en instancia <c>draft</c> (inmutabilidad post-envío).
/// </summary>
public sealed class SubirDocumentoPortalHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage)
{
    public async Task<(AttachmentDto? Result, string? Error)> HandleAsync(
        string token,
        SubirDocumentoPortalInput input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, "not_found");

        var participant = await repo.GetParticipantByTokenHashAsync(PortalToken.Hash(token), ct);
        if (participant is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        if (participant.CompletedAt is not null || now > participant.ExpiresAt)
            return (null, "not_found");

        // GATE de consentimiento: el participante DEBE aceptar antes de subir documentos.
        if (participant.Consent1581At is null)
            return (null, "consent_requerido");

        // Validación de archivo idéntica al flujo autenticado (UploadAttachmentHandler).
        if (input.Content is null || input.SizeBytes <= 0)
            return (null, "missing_file");
        if (string.IsNullOrWhiteSpace(input.Tipo) || !AttachmentRules.ValidTipos.Contains(input.Tipo))
            return (null, "invalid_tipo");
        if (string.IsNullOrWhiteSpace(input.Mimetype) || !AttachmentRules.ValidMimetypes.Contains(input.Mimetype))
            return (null, "invalid_mime");
        if (input.SizeBytes > AttachmentRules.MaxSizeBytes)
            return (null, "file_too_large");

        var instance = participant.ProcedureInstance!;
        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            return (null, "not_draft");

        var tipo = input.Tipo.Trim().ToLowerInvariant();
        var stored = await storage.SaveAsync(instance.Id, tipo, input.Filename ?? "file", input.Content, ct);

        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = participant.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = string.IsNullOrWhiteSpace(input.Filename) ? "file" : input.Filename.Trim(),
            Mimetype = input.Mimetype.Trim().ToLowerInvariant(),
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            StoragePath = stored.StoragePath,
            Source = "portal",
            UploadedAt = now,
        };
        instance.Attachments.Add(attachment);
        // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar INSERT.
        repo.Add(attachment);

        // Auto-marca del checklist (mismo comportamiento que el flujo autenticado).
        ChecklistEstadoJson.AutoMark(instance, tipo);

        await repo.AddEventAsync(EventFactory.DocumentoSubido(participant, tipo), ct);
        await repo.SaveChangesAsync(ct);

        return (UploadAttachmentHandler.ToDto(attachment), null);
    }
}

// ── Handler: finalizar portal (público) ──────────────────────────────────────

/// <summary>
/// Finaliza la participación: marca <c>completed_at</c>, lo que REVOCA el token (uso único — un
/// acceso posterior con el mismo token → not_found). Append de evento <c>portal_finalizado</c>.
/// SEGURIDAD: token inválido/expirado/usado → not_found genérico.
/// </summary>
public sealed class FinalizarPortalHandler(IProcedureInstanceRepository repo)
{
    public async Task<(FinalizarPortalResult? Result, string? Error)> HandleAsync(
        string token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, "not_found");

        var participant = await repo.GetParticipantByTokenHashAsync(PortalToken.Hash(token), ct);
        if (participant is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        if (participant.CompletedAt is not null || now > participant.ExpiresAt)
            return (null, "not_found");

        participant.CompletedAt = now;
        participant.UpdatedAt = now;

        await repo.AddEventAsync(EventFactory.PortalFinalizado(participant), ct);
        await repo.SaveChangesAsync(ct);

        return (new FinalizarPortalResult(now), null);
    }
}

// ── Firma desde el portal (passthrough, reusa la lógica de firma) ────────────

/// <summary>Estado de la firma del participante (URL del proveedor mock + estado), o no-aplica.</summary>
public sealed record PortalFirmaUrlDto(bool Aplica, Guid? SignatureId, string? Estado, string? SignUrl, bool Firmada);

/// <summary>
/// Surface PÚBLICO del estado de firma del participante: localiza la firma de su rol (comprador|
/// vendedor) y devuelve la signUrl/estado del proveedor (mock). No re-implementa reglas de firma.
/// SEGURIDAD: token inválido/expirado/usado → not_found. Si el rol no firma compraventa → no_aplica.
/// </summary>
public sealed class GetFirmaUrlPortalHandler(IProcedureInstanceRepository repo)
{
    public async Task<(PortalFirmaUrlDto? Result, string? Error)> HandleAsync(
        string token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, "not_found");

        var participant = await repo.GetParticipantByTokenHashAsync(PortalToken.Hash(token), ct);
        if (participant is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        if (participant.CompletedAt is not null || now > participant.ExpiresAt)
            return (null, "not_found");

        var instance = participant.ProcedureInstance!;
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var aplica = string.Equals(codigo, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase)
            && participant.Rol is ParticipantRoles.Comprador or ParticipantRoles.Vendedor;
        if (!aplica)
            return (new PortalFirmaUrlDto(Aplica: false, null, null, null, false), null);

        var sig = instance.Signatures.FirstOrDefault(s =>
            string.Equals(s.Parte, participant.Rol, StringComparison.OrdinalIgnoreCase));
        if (sig is null)
            return (new PortalFirmaUrlDto(Aplica: true, null, null, null, false), null);

        var dto = SolicitarFirmaHandler.ToDto(sig);
        return (new PortalFirmaUrlDto(Aplica: true, sig.Id, sig.Estado, dto.SignUrl, dto.Firmada), null);
    }
}

/// <summary>
/// Completa la firma del participante desde el portal — passthrough que REUSA
/// <see cref="SimularFirmaHandler"/> (no duplica reglas de firma). Localiza la firma del rol y delega
/// con el tenant/instancia del participante. SEGURIDAD: token inválido/expirado/usado → not_found.
/// </summary>
public sealed class SimularFirmaPortalHandler(
    IProcedureInstanceRepository repo,
    SimularFirmaHandler inner)
{
    public async Task<(SimularFirmaResult? Result, string? Error)> HandleAsync(
        string token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, "not_found");

        var participant = await repo.GetParticipantByTokenHashAsync(PortalToken.Hash(token), ct);
        if (participant is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        if (participant.CompletedAt is not null || now > participant.ExpiresAt)
            return (null, "not_found");

        var instance = participant.ProcedureInstance!;
        var sig = instance.Signatures.FirstOrDefault(s =>
            string.Equals(s.Parte, participant.Rol, StringComparison.OrdinalIgnoreCase));
        if (sig is null)
            return (null, "firma_no_solicitada");

        // Reusa el handler de firma del flujo autenticado (mismas reglas/estados).
        return await inner.HandleAsync(instance.Id, participant.TenantId, sig.Id, ct);
    }
}

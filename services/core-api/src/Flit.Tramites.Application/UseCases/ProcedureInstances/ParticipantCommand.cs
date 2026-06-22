using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs (contrato Slice 7 Part B — portal de participantes) ────────────────

/// <summary>Vista (gestor autenticado) de un participante invitado al portal.</summary>
public sealed record ParticipantDto(
    Guid Id,
    string Rol,
    string Nombre,
    string Email,
    string? Telefono,
    bool WhatsappOptIn,
    bool ConsentDado,
    string? ConsentVersion,
    DateTimeOffset? Consent1581At,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? LastReminderAt,
    bool Expirado,
    bool Completado);

/// <summary>Resultado de invitar: incluye el token CRUDO (solo aquí) para construir el magic-link.</summary>
public sealed record InvitarParticipanteResult(
    ParticipantDto Participant,
    string Token,
    string MagicLinkPath);

public sealed record ParticipantsResponse(IReadOnlyList<ParticipantDto> Participants);

/// <summary>Entrada para invitar a un participante al portal.</summary>
public sealed record InvitarParticipanteInput(
    string Rol,
    string Nombre,
    string Email,
    string? Telefono,
    bool WhatsappOptIn);

/// <summary>
/// Helpers de token del portal: genera token crudo (base64url 24 bytes) y su hash SHA-256.
/// SEGURIDAD: el token crudo solo se devuelve en la invitación; en BD se persiste SOLO el hash.
/// </summary>
public static class PortalToken
{
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }
}

// ── Handler: invitar participante (autenticado) ──────────────────────────────

/// <summary>
/// Crea un participante del portal y devuelve el token CRUDO (para construir el magic-link
/// <c>/portal/{token}</c>). SEGURIDAD: solo se persiste el SHA-256 del token; el crudo se devuelve
/// una sola vez (no se envía email — en DEV el admin lo entrega). Idempotencia por rol: si existe un
/// participante ACTIVO (no completado y no expirado) para el mismo rol → <c>participante_activo</c>
/// (409); el gestor debe reusarlo o reinvitarlo (rotación explícita) en vez de duplicar. TTL 24h.
/// </summary>
public sealed class InvitarParticipanteHandler(IProcedureInstanceRepository repo)
{
    public async Task<(InvitarParticipanteResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        InvitarParticipanteInput input,
        Guid? createdBy = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Nombre) || string.IsNullOrWhiteSpace(input.Email))
            return (null, "datos_incompletos");

        var rol = NormalizeRol(input.Rol);
        if (rol is null)
            return (null, "rol_invalido");

        var instance = await repo.GetByIdWithParticipantsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;

        // Idempotencia por rol: un participante activo (no completado, no expirado) bloquea duplicar.
        var active = instance.Participants.FirstOrDefault(p =>
            string.Equals(p.Rol, rol, StringComparison.OrdinalIgnoreCase)
            && p.CompletedAt is null
            && now <= p.ExpiresAt);
        if (active is not null)
            return (null, "participante_activo");

        var token = PortalToken.Generate();
        var participant = new ProcedureInstanceParticipant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Rol = rol,
            Nombre = input.Nombre.Trim(),
            Email = input.Email.Trim(),
            Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono.Trim(),
            TokenHash = PortalToken.Hash(token),
            WhatsappOptIn = input.WhatsappOptIn,
            ExpiresAt = now.AddHours(ParticipantRules.TokenTtlHoras),
            CreatedBy = createdBy,
            CreatedAt = now,
        };

        instance.Participants.Add(participant);
        // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar INSERT.
        repo.Add(participant);

        await repo.AddEventAsync(EventFactory.ParticipanteInvitado(participant), ct);
        await repo.SaveChangesAsync(ct);

        var dto = ToDto(participant, now);
        return (new InvitarParticipanteResult(dto, token, $"/portal/{token}"), null);
    }

    internal static string? NormalizeRol(string? rol)
    {
        if (string.IsNullOrWhiteSpace(rol))
            return null;
        var r = rol.Trim().ToLowerInvariant();
        return r is ParticipantRoles.Comprador or ParticipantRoles.Vendedor or ParticipantRoles.Mandatario
            ? r : null;
    }

    internal static ParticipantDto ToDto(ProcedureInstanceParticipant p, DateTimeOffset now) =>
        new(p.Id, p.Rol, p.Nombre, p.Email, p.Telefono, p.WhatsappOptIn,
            p.Consent1581At is not null, p.ConsentVersion, p.Consent1581At,
            p.ExpiresAt, p.CompletedAt, p.LastReminderAt,
            Expirado: p.CompletedAt is null && now > p.ExpiresAt,
            Completado: p.CompletedAt is not null);
}

// ── Handler: listar participantes (autenticado) ──────────────────────────────

/// <summary>Lista los participantes del portal de una instancia (vista del gestor).</summary>
public sealed class ListParticipantesHandler(IProcedureInstanceRepository repo)
{
    public async Task<(ParticipantsResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithParticipantsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        var dtos = instance.Participants
            .OrderBy(p => p.CreatedAt)
            .Select(p => InvitarParticipanteHandler.ToDto(p, now))
            .ToList();
        return (new ParticipantsResponse(dtos), null);
    }
}

// ── Handler: reinvitar participante (autenticado) ────────────────────────────

/// <summary>
/// Re-invita a un participante: ROTA el token (nuevo crudo + nuevo hash), reinicia la expiración y
/// marca <c>last_reminder_at</c>. Cooldown: rechaza si hubo un recordatorio hace menos de 24h
/// (<c>reminder_cooldown</c>). No se puede reinvitar a un participante ya completado
/// (<c>ya_completado</c>). Devuelve el token CRUDO nuevo (el anterior queda invalidado).
/// </summary>
public sealed class ReinvitarParticipanteHandler(IProcedureInstanceRepository repo)
{
    public async Task<(InvitarParticipanteResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid participantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithParticipantsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var participant = instance.Participants.FirstOrDefault(p => p.Id == participantId);
        if (participant is null)
            return (null, "participant_not_found");

        if (participant.CompletedAt is not null)
            return (null, "ya_completado");

        var now = DateTimeOffset.UtcNow;
        if (participant.LastReminderAt is { } last
            && now - last < TimeSpan.FromHours(ParticipantRules.ReminderCooldownHoras))
            return (null, "reminder_cooldown");

        // Rotación del token: invalida el anterior (nuevo hash) y reinicia la ventana.
        var token = PortalToken.Generate();
        participant.TokenHash = PortalToken.Hash(token);
        participant.ExpiresAt = now.AddHours(ParticipantRules.TokenTtlHoras);
        participant.LastReminderAt = now;
        participant.UpdatedAt = now;

        await repo.AddEventAsync(EventFactory.ParticipanteReinvitado(participant), ct);
        await repo.SaveChangesAsync(ct);

        var dto = InvitarParticipanteHandler.ToDto(participant, now);
        return (new InvitarParticipanteResult(dto, token, $"/portal/{token}"), null);
    }
}

/// <summary>Fábrica de eventos de bitácora del portal (payload jsonb mínimo, sin PII sensible).</summary>
internal static class EventFactory
{
    public static ProcedureInstanceEvent ParticipanteInvitado(ProcedureInstanceParticipant p) =>
        Build(p, "participante_invitado", new { rol = p.Rol });

    public static ProcedureInstanceEvent ParticipanteReinvitado(ProcedureInstanceParticipant p) =>
        Build(p, "participante_reinvitado", new { rol = p.Rol });

    public static ProcedureInstanceEvent Consentimiento1581(ProcedureInstanceParticipant p) =>
        Build(p, "consentimiento_1581", new { rol = p.Rol, version = p.ConsentVersion });

    public static ProcedureInstanceEvent DocumentoSubido(ProcedureInstanceParticipant p, string tipo) =>
        Build(p, "documento_subido", new { rol = p.Rol, tipo });

    public static ProcedureInstanceEvent PortalFinalizado(ProcedureInstanceParticipant p) =>
        Build(p, "portal_finalizado", new { rol = p.Rol });

    private static ProcedureInstanceEvent Build(ProcedureInstanceParticipant p, string tipo, object payload) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = p.TenantId,
            ProcedureInstanceId = p.ProcedureInstanceId,
            Tipo = tipo,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
        };
}

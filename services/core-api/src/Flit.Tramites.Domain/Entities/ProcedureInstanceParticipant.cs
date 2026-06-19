namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Participante de un trámite (comprador|vendedor|mandatario) que accede al PORTAL PÚBLICO vía
/// magic-link para completar su parte: consentimiento Ley 1581 → documentos → biométrica → firma.
/// Slice 7 — Part B (portal público de participantes).
///
/// SEGURIDAD: el token crudo NUNCA se persiste — solo su SHA-256 (<see cref="TokenHash"/>). El token
/// crudo solo se devuelve UNA vez en la respuesta de invitación (no se envía email; en DEV el admin
/// recibe el magic-link). Token de un solo uso: <see cref="CompletedAt"/> lo revoca al finalizar.
/// Re-invitar rota el token (nuevo hash + nueva expiración). TTL 24h.
/// </summary>
public sealed class ProcedureInstanceParticipant
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>'comprador' | 'vendedor' | 'mandatario'.</summary>
    public string Rol { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefono { get; set; }

    /// <summary>SHA-256 (hex) del token enviado por magic-link. El token crudo nunca se persiste.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Opt-in explícito para recordatorios por WhatsApp. Default false.</summary>
    public bool WhatsappOptIn { get; set; }

    // ── Consentimiento Ley 1581 de 2012 (prueba de auditoría) ──
    /// <summary>Marca temporal de aceptación del consentimiento. Null hasta que el participante acepta.</summary>
    public DateTimeOffset? Consent1581At { get; set; }

    /// <summary>Versión del texto de consentimiento aceptada (p. ej. '2026-06-19-v1').</summary>
    public string? ConsentVersion { get; set; }

    /// <summary>IP del cliente al aceptar (prueba de auditoría).</summary>
    public string? ConsentIp { get; set; }

    /// <summary>User-Agent del cliente al aceptar, truncado a ~120 chars (prueba de auditoría).</summary>
    public string? ConsentUserAgent { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Marca de finalización; revoca el token (uso único). Null mientras está activo.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Último recordatorio enviado (cooldown de re-invitación).</summary>
    public DateTimeOffset? LastReminderAt { get; set; }

    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}

/// <summary>Roles válidos del participante del portal.</summary>
public static class ParticipantRoles
{
    public const string Comprador = "comprador";
    public const string Vendedor = "vendedor";
    public const string Mandatario = "mandatario";
}

/// <summary>Reglas de negocio del portal de participantes (compartidas Application/Domain).</summary>
public static class ParticipantRules
{
    public const int TokenTtlHoras = 24;

    /// <summary>Cooldown mínimo entre recordatorios/reinvitaciones (horas).</summary>
    public const int ReminderCooldownHoras = 24;

    /// <summary>Versión vigente del texto de consentimiento Ley 1581 (versionado para auditoría).</summary>
    public const string ConsentVersion = "2026-06-19-v1";

    /// <summary>Longitud máxima del User-Agent almacenado como prueba (se trunca).</summary>
    public const int UserAgentMaxLength = 120;

    /// <summary>
    /// Texto de consentimiento informado (Ley 1581 de 2012 — protección de datos personales — y
    /// mención a la firma electrónica de la Ley 527 de 1999). Versionado por <see cref="ConsentVersion"/>.
    /// </summary>
    public const string ConsentText =
        "Autorizo de manera libre, previa, expresa e informada a Flit S.A.S. y a su cliente para " +
        "recolectar, almacenar, usar y tratar mis datos personales —incluidos datos biométricos e " +
        "imágenes de mi documento de identidad— con la finalidad de adelantar el trámite de tránsito " +
        "correspondiente, conforme a la Ley 1581 de 2012, el Decreto 1377 de 2013 y la política de " +
        "tratamiento de datos aplicable. Declaro conocer mis derechos a conocer, actualizar, rectificar " +
        "y suprimir mis datos y a revocar esta autorización. Asimismo, acepto que mi firma realizada a " +
        "través de este portal constituye una firma electrónica con plenos efectos jurídicos de " +
        "conformidad con la Ley 527 de 1999.";
}

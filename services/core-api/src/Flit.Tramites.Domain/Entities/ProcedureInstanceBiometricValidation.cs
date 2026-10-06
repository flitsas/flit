using Flit.Queries.Domain.Time;
using Flit.Tramites.Domain.Identity;

namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Validación biométrica remota de una parte de un trámite (selfie + cédula frontal/reverso).
/// Slice 6 — biométrica (mock). El scoring es determinista y mockeado; la integración real con
/// Anthropic se diferirá (ver IBiometricScorer en Application).
/// </summary>
public sealed class ProcedureInstanceBiometricValidation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// HU #10865 — nullable para soportar prevalidaciones standalone (sin trámite).
    /// Invariante: <c>ProcedureInstanceId IS NOT NULL OR PersonId IS NOT NULL</c> (CHECK en BD).
    /// </summary>
    public Guid? ProcedureInstanceId { get; set; }

    /// <summary>
    /// HU #10865 — FK a <see cref="Person"/>. Null en validaciones históricas (backcompat);
    /// NOT NULL en filas nuevas (tanto standalone como ligadas a trámite).
    /// </summary>
    public Guid? PersonId { get; set; }

    /// <summary>'comprador' | 'vendedor' | 'mandatario'. Null en matrícula inicial o en prevalidación standalone.</summary>
    public string? PartyRole { get; set; }

    /// <summary>
    /// HU #13246 (Feature #13245, Épica #13090) — ficha del mandatario (<c>admin.mandate_signers.id</c>) para la que se
    /// lanzó esta validación. Solo se rellena con <see cref="PartyRole"/> = <c>mandatario</c> (CHECK en BD): la validación
    /// del mandatario es EXCLUSIVA, solo cuenta para esa ficha y no entra en las consultas por documento del trámite, la
    /// prevalidación ni el módulo Identidad. Sin persona ni trámite (el ancla es esta ficha).
    /// </summary>
    public Guid? MandateSignerId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Bug #12376, defecto 3 — correo CON EL QUE SE CREÓ esta validación (registro), estampado UNA vez y
    /// jamás actualizado después. <see cref="Email"/> sigue siendo el campo OPERATIVO (a donde
    /// efectivamente se envía el link/OTP: lo actualiza el reenvío administrativo,
    /// <c>AdminReenviarValidacionIdentidadHandler</c>); este campo es solo para que el tracking del
    /// trámite pueda seguir mostrando el correo original del registro aunque se haya reenviado a otro
    /// destino. Vacío en filas anteriores a esta migración (backfill = <see cref="Email"/> al momento de
    /// migrar, mejor esfuerzo).
    /// </summary>
    public string RegisteredEmail { get; set; } = string.Empty;

    /// <summary>enviado | en_proceso | aprobado | rechazado | expirado | pendiente_envio | error_envio |
    /// manual_activo | pendiente_revision_manual (ver <see cref="BiometricEstados"/>; CHECK en BD, DDL 130).</summary>
    public string Status { get; set; } = BiometricEstados.Enviado;

    /// <summary>SHA-256 (hex) del token enviado por magic-link. El token crudo nunca se persiste.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }

    // ── Proveedor de validación de identidad (HU #10233 — Kyverum Verify) ────────

    /// <summary>'mock' | 'kyverum' | 'migracion_v1' | 'manual' (ver <see cref="BiometricProviders"/>). Default 'mock' (flujo determinista de 3 fotos). 'kyverum' = validación
    /// remota delegada al proveedor externo Kyverum Verify (captura + webhook firmado).</summary>
    public string Provider { get; set; } = BiometricProviders.Mock;

    /// <summary>Id de la verificación en Kyverum (correlación con el webhook). Null cuando provider='mock'.</summary>
    public string? KyverumVerificationId { get; set; }

    /// <summary>URL de captura que abre el participante para completar la validación en Kyverum.</summary>
    public string? CaptureUrl { get; set; }

    /// <summary>Secreto HMAC del webhook CIFRADO con Data Protection API. NUNCA se persiste en claro
    /// ni se expone en DTOs/logs. Se descifra solo para verificar la firma del webhook entrante.</summary>
    public string? WebhookSecretEncrypted { get; set; }

    /// <summary>Estado crudo reportado por Kyverum (p.ej. 'approved'|'rejected'|'pending'). Trazabilidad.</summary>
    public string? ProviderStatus { get; set; }

    /// <summary>Payload del proveedor SANITIZADO (sin PII cruda ni secretos), en jsonb. Trazabilidad.</summary>
    public string? ProviderPayload { get; set; }

    /// <summary>
    /// Serie/hash del certificado de la validación biométrica que reporta Kyverum (<c>firmaSerie</c> del
    /// subject aprobado, HU #10488). Es el identificador verificable del certificado de identidad emitido por
    /// el proveedor externo; se estampa UNA vez al aprobar (webhook o, si Kyverum lo expone en el GET,
    /// reconciliación) y alimenta el sello de firma del FUR. Null en mock o mientras no haya aprobación.
    /// </summary>
    public string? CertificateHash { get; set; }

    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = BiometricRules.MaxIntentos;

    /// <summary>
    /// Clave del ÚLTIMO intento YA CONTADO en <see cref="Attempts"/> (Kyverum), tomada del CUERPO del webhook
    /// (<c>data.closedAt</c> ?? <c>ts</c> ?? <c>requestId</c>). El conteo de intentos es AUTORITATIVO por webhook:
    /// cada intento fallido dispara UN <c>validation.rejected</c> con una clave estable en su cuerpo inmutable,
    /// mientras que un redelivery del MISMO evento repite la clave. Se cuenta una sola vez comparando exacto
    /// (string) contra este valor; el worker/poll de reconciliación ya NO cuenta (evita el doble-conteo
    /// webhook+poll que inflaba los intentos). Null hasta el primer intento fallido.
    /// </summary>
    public string? LastAttemptAt { get; set; }

    /// <summary>
    /// Cuántas veces el worker de reconciliación (<c>IdentityValidationReconcileProcessor</c>) ya sondeó a
    /// Kyverum en la ventana de espera ACTUAL. El worker solo reclama validaciones con
    /// <c>ReconcilePollCount &lt; <see cref="BiometricRules.KyverumMaxReconcilePolls"/></c> (3): así, si el
    /// cliente hace un intento y se queda quieto, el worker sondea a lo sumo 3 veces (~6 min) y luego CALLA en
    /// vez de pegarle a Kyverum cada 2 min durante horas. Se REINICIA a 0 cuando hay señal de actividad nueva:
    /// un intento nuevo por webhook, el (re)envío de la validación, o un reconcile manual del gestor.
    /// </summary>
    public int ReconcilePollCount { get; set; }

    public int? Score { get; set; }
    public string? Detail { get; set; }

    public string? FacePhotoPath { get; set; }
    public string? IdFrontPhotoPath { get; set; }
    public string? IdBackPhotoPath { get; set; }

    /// <summary>
    /// Path opaco en storage del PNG recortado de la rúbrica del certificado Kyverum (ADR-0054).
    /// Null hasta extraerse o si el recorte no aplica (mock, PDF sin imagen). PII alta.
    /// </summary>
    public string? SignatureImagePath { get; set; }

    /// <summary>SHA-256 (hex minúsculas) del PNG de <see cref="SignatureImagePath"/>. Null si no hay path.</summary>
    public string? SignatureImageSha256 { get; set; }

    public DateTimeOffset? ValidatedAt { get; set; }

    /// <summary>
    /// Fecha de fin de vigencia de la identidad APROBADA: medianoche (hora Colombia, UTC-5) del día
    /// <c>ValidatedAt + VigenciaDias</c>. La ESTAMPA el código al aprobar (ver <see cref="Approve"/>), NO la
    /// BD — es un valor absoluto que solo depende de <c>ValidatedAt</c>. NULL mientras no haya aprobación.
    /// Los "días restantes" NO se persisten: se calculan al leer con
    /// <see cref="BiometricRules.DiasRestantesVigencia"/> (siempre frescos, sin job).
    /// </summary>
    public DateTimeOffset? ValidUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// HU #10943 (CF-03) — cuántas veces se reenvió esta validación (manual o automático por cambio de
    /// correo). Solo bitácora: ya no limita el reenvío (el tope D10 se eliminó a pedido del producto).
    /// </summary>
    public int ResendCount { get; set; }

    /// <summary>
    /// HU #10943 (CF-03) — momento del ÚLTIMO reenvío (manual o por cambio de correo). Solo bitácora:
    /// ya no alimenta ningún cooldown (el cooldown D10 se eliminó a pedido del producto). Null si nunca
    /// se ha reenviado.
    /// </summary>
    public DateTimeOffset? LastResentAt { get; set; }

    // ── Identidad manual (HU #13283, Feature #13280 A1, Épica #13202; DDL 130) ───────────────
    // Todas NULL salvo que el flujo manual (o la aprobación) las estampe. La firma trazada reutiliza
    // SignatureImagePath / SignatureImageSha256 (ADR-0054): no hay columna de firma propia.

    /// <summary>'automatica' | 'manual' (<see cref="BiometricApprovalOrigins"/>). Null mientras no esté aprobada;
    /// el backfill del DDL 130 dejó 'automatica' en las ya aprobadas.</summary>
    public string? ApprovalOrigin { get; set; }

    /// <summary>Usuario (identity.users.id) que activó el flujo manual. Sin FK, como created_by.</summary>
    public Guid? ManualActivatedBy { get; set; }

    /// <summary>Momento de activación del flujo manual.</summary>
    public DateTimeOffset? ManualActivatedAt { get; set; }

    /// <summary>Momento en que la persona aceptó el consentimiento en el flujo manual.</summary>
    public DateTimeOffset? ConsentAt { get; set; }

    /// <summary>IP desde la que se aceptó el consentimiento. PII media (Habeas Data).</summary>
    public string? ConsentIp { get; set; }

    /// <summary>Versión del texto de consentimiento aceptado.</summary>
    public string? ConsentTextVersion { get; set; }

    /// <summary>Usuario (identity.users.id) que revisó la validación manual. Sin FK, como created_by.</summary>
    public Guid? ReviewedBy { get; set; }

    /// <summary>Momento de la revisión humana.</summary>
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>Código (lista cerrada, constante en código) del motivo de rechazo de la revisión manual.</summary>
    public string? RejectionReasonCode { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }

    /// <summary>
    /// Bug #13055 — el trámite dueño está anulado o revocado: la validación conserva su estado y no
    /// admite resultados tardíos. Requiere la navegación <see cref="ProcedureInstance"/> cargada; una
    /// prevalidación standalone (sin trámite) nunca se congela.
    /// </summary>
    public bool CongeladaPorTramite =>
        ProcedureInstance is not null
        && Flit.Tramites.Domain.Tramites.Estados.TramiteEstado.CongelaValidacionIdentidad(ProcedureInstance.Status);

    /// <summary>HU #10865 — navegación a la entidad persona del tenant.</summary>
    public Person? Person { get; set; }

    /// <summary>
    /// HU #13284 (Feature #13280 A2) — ¿se puede activar el flujo manual sobre esta validación? Solo si NO está
    /// aprobada y vigente (<see cref="BiometricRules.EsAprobadaVigente"/>, la misma regla de vigencia de siempre):
    /// rechazada, expirada, vencida (aprobada fuera de ventana) o en curso sí. No evalúa el trámite dueño
    /// (<see cref="CongeladaPorTramite"/>): eso lo decide el caso de uso.
    /// </summary>
    public bool PuedeActivarFlujoManual(DateTimeOffset now) => !BiometricRules.EsAprobadaVigente(this, now);

    /// <summary>
    /// HU #13284 — activa el flujo manual SOBRE ESTA MISMA FILA (una sola fuente de vigencia, ADR-0050): pasa a
    /// <see cref="BiometricProviders.Manual"/> / <see cref="BiometricEstados.ManualActivo"/>, deja el enlace de captura
    /// vigente <see cref="BiometricRules.TokenTtlHoras"/> horas (<paramref name="tokenHash"/> es el SHA-256 hex del
    /// token; el crudo jamás entra a la entidad) y estampa quién y cuándo lo activó.
    /// <para>
    /// Cancela la verificación Kyverum para FLIT: limpia <see cref="KyverumVerificationId"/>, <see cref="CaptureUrl"/>
    /// y <see cref="WebhookSecretEncrypted"/> (sin secreto ni id, un webhook posterior no se puede verificar ni
    /// correlacionar) y reinicia los contadores propios de Kyverum. El id externo NO se conserva aquí: el caso de uso lo
    /// deja en la bitácora de auditoría. Todo lo demás (fotos, aprobación previa vencida, consentimiento y revisión de un
    /// ciclo manual anterior) se conserva como historia: las pisan la nueva captura y la nueva revisión.
    /// </para>
    /// </summary>
    /// <exception cref="IdentidadManualNoActivableException">Aprobada y vigente.</exception>
    public void ActivarFlujoManual(Guid userId, DateTimeOffset now, string tokenHash)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("El usuario que activa el flujo manual es obligatorio.", nameof(userId));
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != 64)
            throw new ArgumentException("El token debe guardarse como hash SHA-256 (64 hex).", nameof(tokenHash));
        if (!PuedeActivarFlujoManual(now))
            throw new IdentidadManualNoActivableException();

        Provider = BiometricProviders.Manual;
        Status = BiometricEstados.ManualActivo;
        TokenHash = tokenHash;
        ExpiresAt = now.AddHours(BiometricRules.TokenTtlHoras);
        ManualActivatedBy = userId;
        ManualActivatedAt = now;
        UpdatedAt = now;

        // Cancelación de Kyverum para FLIT.
        KyverumVerificationId = null;
        CaptureUrl = null;
        WebhookSecretEncrypted = null;
        ProviderStatus = null;
        Attempts = 0;
        ReconcilePollCount = 0;
        LastAttemptAt = null;
    }

    /// <summary>
    /// HU #13299 — ¿el flujo manual espera una captura del cliente con un enlace emitido? Dos situaciones: <c>manual_activo</c>
    /// (activación o regeneración) y <c>rechazado</c> tras una revisión manual (<see cref="RejectionReasonCode"/> no nulo): el rechazo
    /// deja el estado en <c>rechazado</c> y emite en la misma operación un enlace nuevo para repetir la captura. Un <c>rechazado</c>
    /// manual SIN motivo no es una situación de este flujo y no espera nada.
    /// </summary>
    public bool EsperaCapturaManual =>
        string.Equals(Provider, BiometricProviders.Manual, StringComparison.Ordinal)
        && (string.Equals(Status, BiometricEstados.ManualActivo, StringComparison.Ordinal)
            || (string.Equals(Status, BiometricEstados.Rechazado, StringComparison.Ordinal) && RejectionReasonCode is not null));

    /// <summary>
    /// HU #13287 (Feature #13280 A5) — ¿se puede regenerar el enlace de captura? Solo con el flujo manual esperando captura
    /// (<see cref="EsperaCapturaManual"/>: <see cref="BiometricEstados.ManualActivo"/> o <c>rechazado</c> con motivo, HU #13299: el
    /// Super Admin puede reenviar el enlace si el cliente no lo recibió). Un enlace vencido (más de
    /// <see cref="BiometricRules.TokenTtlHoras"/> h) sí se regenera: es el caso de uso principal.
    /// </summary>
    public bool PuedeRegenerarEnlaceManual => EsperaCapturaManual;

    /// <summary>
    /// HU #13287 — emite un enlace de captura NUEVO que REEMPLAZA al anterior: <paramref name="tokenHash"/> (SHA-256 hex del
    /// token nuevo; el crudo jamás entra a la entidad) sustituye a <see cref="TokenHash"/>, así el token viejo deja de
    /// encontrarse por hash, y la vigencia se reinicia a <see cref="BiometricRules.TokenTtlHoras"/> horas desde
    /// <paramref name="now"/>. Cuenta el reenvío (<see cref="ResendCount"/>, <see cref="LastResentAt"/>). No toca el estado,
    /// quién ni cuándo se activó, ni las fotos o el consentimiento (en <c>rechazado</c> sigue <c>rechazado</c>).
    /// </summary>
    /// <exception cref="FlujoManualNoActivoException">No espera captura (<see cref="EsperaCapturaManual"/>).</exception>
    public void RegenerarEnlaceManual(DateTimeOffset now, string tokenHash)
    {
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != 64)
            throw new ArgumentException("El token debe guardarse como hash SHA-256 (64 hex).", nameof(tokenHash));
        if (!PuedeRegenerarEnlaceManual)
            throw new Flit.Tramites.Domain.Identity.FlujoManualNoActivoException();
        if (string.Equals(tokenHash, TokenHash, StringComparison.Ordinal))
            throw new ArgumentException("El enlace regenerado debe ser distinto del anterior.", nameof(tokenHash));

        TokenHash = tokenHash;
        ExpiresAt = now.AddHours(BiometricRules.TokenTtlHoras);
        ResendCount++;
        LastResentAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// HU #13289 — estado de la sesión de captura manual en <paramref name="now"/>. Solo el flujo manual que espera captura
    /// (<see cref="EsperaCapturaManual"/>: <c>manual_activo</c>, o <c>rechazado</c> con motivo tras una revisión — HU #13299) y dentro
    /// de la ventana de su enlace es <see cref="ManualCaptureSessionState.Vigente"/>; con el enlace vencido es
    /// <see cref="ManualCaptureSessionState.Vencida"/> también en <c>rechazado</c>.
    /// </summary>
    public ManualCaptureSessionState EstadoSesionManual(DateTimeOffset now)
    {
        if (!string.Equals(Provider, BiometricProviders.Manual, StringComparison.Ordinal))
            return ManualCaptureSessionState.NoManual;
        if (!EsperaCapturaManual)
            return ManualCaptureSessionState.EstadoInvalido;
        return now > ExpiresAt ? ManualCaptureSessionState.Vencida : ManualCaptureSessionState.Vigente;
    }

    /// <summary>
    /// HU #13289/#13290 — ¿hay consentimiento del ciclo manual ACTUAL? La reactivación (A2) NO limpia el consentimiento de un
    /// ciclo anterior, así que solo vale el aceptado a partir de la última activación
    /// (<c>consent_at &gt;= manual_activated_at</c>).
    /// </summary>
    public bool TieneConsentimientoManualVigente =>
        ConsentAt is { } consent && ManualActivatedAt is { } activated && consent >= activated;

    /// <summary>
    /// HU #13289 — constancia del consentimiento biométrico: fecha/hora (del servidor), IP resuelta por el servidor (nunca la
    /// del cuerpo) y versión del texto. SOBRESCRIBE la de un ciclo manual previo. Solo con la sesión vigente.
    /// </summary>
    /// <exception cref="ManualCaptureStateException">Sesión no vigente (vencida, usada o no manual).</exception>
    /// <exception cref="ArgumentException">Versión vacía o distinta de la vigente (<see cref="ManualCaptureConsent.TextVersion"/>).</exception>
    public void RegistrarConsentimientoManual(string textVersion, string? clientIp, DateTimeOffset now)
    {
        AsegurarSesionManualVigente(now);
        if (!string.Equals(textVersion, ManualCaptureConsent.TextVersion, StringComparison.Ordinal))
            throw new ArgumentException("La versión del texto de consentimiento no es la vigente.", nameof(textVersion));

        ConsentAt = now;
        ConsentIp = string.IsNullOrWhiteSpace(clientIp) ? null : clientIp.Trim();
        ConsentTextVersion = textVersion;
        UpdatedAt = now;
    }

    /// <summary>
    /// HU #13290 — registra la captura recibida: rutas de rostro, anverso, reverso y firma (ADR-0054: la firma reutiliza
    /// <see cref="SignatureImagePath"/>/<see cref="SignatureImageSha256"/>) y pasa a
    /// <see cref="BiometricEstados.PendienteRevisionManual"/>. Solo desde <see cref="BiometricEstados.ManualActivo"/> o desde
    /// <c>rechazado</c> con motivo (repetición tras un rechazo, HU #13299), con la sesión vigente y consentimiento del ciclo actual; consume el enlace (un segundo envío ya no es
    /// <c>manual_activo</c>). Las rutas del intento previo se pisan aquí, pero los archivos NO se borran (el caso de uso las
    /// deja en auditoría).
    /// </summary>
    /// <exception cref="ManualCaptureStateException">Sesión no vigente o sin consentimiento del ciclo actual.</exception>
    /// <exception cref="ArgumentException">Falta alguna ruta o el hash de la firma no es SHA-256.</exception>
    public void RegistrarCapturaManual(
        string facePath, string idFrontPath, string idBackPath, string signaturePath, string signatureSha256, DateTimeOffset now)
    {
        AsegurarSesionManualVigente(now);
        if (!TieneConsentimientoManualVigente)
            throw new ManualCaptureStateException(ManualCaptureStateCodes.ConsentimientoRequerido);
        if (string.IsNullOrWhiteSpace(facePath) || string.IsNullOrWhiteSpace(idFrontPath)
            || string.IsNullOrWhiteSpace(idBackPath) || string.IsNullOrWhiteSpace(signaturePath))
            throw new ArgumentException("La captura manual exige las 4 rutas de imagen.");
        if (string.IsNullOrWhiteSpace(signatureSha256) || signatureSha256.Length != 64)
            throw new ArgumentException("El hash de la firma debe ser SHA-256 (64 hex).", nameof(signatureSha256));

        FacePhotoPath = facePath;
        IdFrontPhotoPath = idFrontPath;
        IdBackPhotoPath = idBackPath;
        SignatureImagePath = signaturePath;
        SignatureImageSha256 = signatureSha256;
        Status = BiometricEstados.PendienteRevisionManual;
        // HU #13299 — la captura nueva abre una revisión nueva: el motivo y la revisión (quién y cuándo) del rechazo previo, que se
        // conservan mientras el cliente repite la captura, se LIMPIAN aquí; la historia queda en la auditoría (manual_rechazado).
        RejectionReasonCode = null;
        ReviewedBy = null;
        ReviewedAt = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// HU #13298/#13299 (Feature #13282 C) — ¿espera revisión humana? Solo el flujo manual con la captura ya recibida
    /// (<see cref="BiometricProviders.Manual"/> + <see cref="BiometricEstados.PendienteRevisionManual"/>).
    /// </summary>
    public bool PuedeRevisarManual =>
        string.Equals(Provider, BiometricProviders.Manual, StringComparison.Ordinal)
        && string.Equals(Status, BiometricEstados.PendienteRevisionManual, StringComparison.Ordinal);

    /// <summary>
    /// HU #13298 — sella la aprobación MANUAL de una validación que YA pasó por <see cref="Approve"/> (el caso de uso la aprueba por
    /// el mismo camino que una aprobación de Kyverum, <c>IdentityValidationResultApplier</c>, para que ValidatedAt/ValidUntil y el
    /// evento de completado salgan de un solo punto): origen <c>manual</c> y quién/cuándo revisó. No toca la vigencia.
    /// </summary>
    /// <exception cref="ArgumentException">Revisor vacío.</exception>
    /// <exception cref="InvalidOperationException">No es una validación manual aprobada.</exception>
    public void SellarAprobacionManual(Guid reviewerId, DateTimeOffset now)
    {
        if (reviewerId == Guid.Empty)
            throw new ArgumentException("El usuario que revisa es obligatorio.", nameof(reviewerId));
        if (!string.Equals(Provider, BiometricProviders.Manual, StringComparison.Ordinal)
            || !string.Equals(Status, BiometricEstados.Aprobado, StringComparison.Ordinal))
            throw new InvalidOperationException("Solo una validación manual ya aprobada se sella como aprobada manualmente.");

        ApprovalOrigin = BiometricApprovalOrigins.Manual;
        ReviewedBy = reviewerId;
        ReviewedAt = now;
        RejectionReasonCode = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// HU #13299 — rechaza la revisión con un motivo de la lista cerrada (<see cref="ManualRejectionReasons"/>) y, en la MISMA
    /// operación, emite un enlace nuevo para repetir la captura. La fila queda en <see cref="BiometricEstados.Rechazado"/> (visible
    /// como «Rechazada»; NO vuelve a <c>manual_activo</c>): registra <see cref="RejectionReasonCode"/>, <see cref="ReviewedBy"/> y
    /// <see cref="ReviewedAt"/> (se conservan hasta que la siguiente captura los limpie, ver <see cref="RegistrarCapturaManual"/>) y
    /// deja el enlace nuevo (<paramref name="tokenHash"/>, SHA-256 hex; el crudo jamás entra a la entidad) vigente
    /// <see cref="BiometricRules.TokenTtlHoras"/> horas. Con ese enlace la sesión de captura cuenta como vigente
    /// (<see cref="EsperaCapturaManual"/>). Sin tope de intentos.
    /// <para>
    /// Abre un CICLO nuevo: <see cref="ManualActivatedAt"/> pasa a <paramref name="now"/> (como una activación), así el consentimiento
    /// del ciclo anterior ya no vale y la persona lo acepta de nuevo, y ese consentimiento nuevo sobrescribe al anterior.
    /// <see cref="ManualActivatedBy"/> no cambia. Las rutas de imagen anteriores se conservan (decisión del PO).
    /// </para>
    /// </summary>
    /// <exception cref="RevisionManualNoPendienteException">No está en <c>pendiente_revision_manual</c>.</exception>
    /// <exception cref="ArgumentException">Revisor vacío, motivo fuera de la lista o token mal formado/repetido.</exception>
    public void RechazarRevisionManual(Guid reviewerId, string reasonCode, DateTimeOffset now, string tokenHash)
    {
        if (reviewerId == Guid.Empty)
            throw new ArgumentException("El usuario que revisa es obligatorio.", nameof(reviewerId));
        if (!ManualRejectionReasons.IsValid(reasonCode))
            throw new ArgumentException("El motivo de rechazo no está en la lista cerrada.", nameof(reasonCode));
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != 64)
            throw new ArgumentException("El token debe guardarse como hash SHA-256 (64 hex).", nameof(tokenHash));
        if (!PuedeRevisarManual)
            throw new RevisionManualNoPendienteException();
        if (string.Equals(tokenHash, TokenHash, StringComparison.Ordinal))
            throw new ArgumentException("El enlace nuevo debe ser distinto del anterior.", nameof(tokenHash));

        Status = BiometricEstados.Rechazado;
        RejectionReasonCode = reasonCode;
        ReviewedBy = reviewerId;
        ReviewedAt = now;

        TokenHash = tokenHash;
        ExpiresAt = now.AddHours(BiometricRules.TokenTtlHoras);
        ManualActivatedAt = now;
        UpdatedAt = now;
    }

    private void AsegurarSesionManualVigente(DateTimeOffset now)
    {
        switch (EstadoSesionManual(now))
        {
            case ManualCaptureSessionState.Vigente:
                return;
            case ManualCaptureSessionState.Vencida:
                throw new ManualCaptureStateException(ManualCaptureStateCodes.Expirada);
            default:
                throw new ManualCaptureStateException(ManualCaptureStateCodes.EstadoInvalido);
        }
    }

    /// <summary>
    /// Marca la validación como APROBADA en <paramref name="now"/>: setea estado + fecha de aprobación y
    /// ESTAMPA la fecha de fin de vigencia (<c>now + VigenciaDias</c>, medianoche Colombia). Punto ÚNICO de
    /// aprobación: garantiza que <see cref="ValidUntil"/> quede siempre en sync con <see cref="ValidatedAt"/>
    /// sin depender de la BD. El reuso de identidad NO usa este método: HEREDA <c>ValidatedAt</c> +
    /// <c>ValidUntil</c> de la validación fuente (conserva el vencimiento original, no reinicia el reloj).
    /// </summary>
    public void Approve(DateTimeOffset now)
    {
        Status = BiometricEstados.Aprobado;
        ValidatedAt = now;
        ValidUntil = BiometricRules.FechaFinVigencia(now);
        UpdatedAt = now;
    }
}

/// <summary>Proveedores de validación de identidad (HU #10233).</summary>
public static class BiometricProviders
{
    /// <summary>Scorer determinista local (3 fotos). Default — preserva el flujo Slice 6.</summary>
    public const string Mock = "mock";

    /// <summary>Kyverum Verify: captura remota + webhook firmado (HMAC-SHA256).</summary>
    public const string Kyverum = "kyverum";

    /// <summary>
    /// La validación NO la hizo V2: la hizo V1 y la migración la trajo como hecho consumado (biométrica
    /// aprobada en V1 o firma física aceptada por el gestor). Aquí no hubo captura, ni proveedor externo,
    /// ni score: es el registro de algo que ya ocurrió en el sistema anterior.
    /// <para>
    /// Existe como proveedor propio —y no reusando <see cref="Mock"/>— por una razón de negocio, no de
    /// estética: una identidad migrada vale para SU trámite y para ninguno más. El reuso de identidad entre
    /// trámites (HU #10350) busca por documento cualquier fila aprobada y vigente del tenant, así que sin
    /// una marca que la distinga, una identidad traída de V1 quedaría apalancando trámites nuevos de V2 que
    /// nunca validaron a nadie. Las dos consultas de reuso
    /// (<c>FindVigenteApprovedByDocumentAsync</c> y <c>ListVigenteApprovedIdentityKeysAsync</c>) la excluyen
    /// explícitamente.
    /// </para>
    /// </summary>
    public const string MigracionV1 = "migracion_v1";

    /// <summary>
    /// Identidad manual (Épica #13202): el flujo manual cancela la verificación de Kyverum y la persona captura
    /// fotos, documento y firma por un enlace propio; un humano revisa. No hay proveedor externo ni score.
    /// Como <see cref="MigracionV1"/>, no debe apalancar el reuso automático de identidad sin pasar por la regla
    /// de aprobación (hoy solo cuenta como aprobada la fila en estado <see cref="BiometricEstados.Aprobado"/>).
    /// </summary>
    public const string Manual = "manual";

    /// <summary>Todos los valores aceptados por <c>ck_biometric_validations_provider</c>.</summary>
    public static readonly IReadOnlyList<string> Todos = [Mock, Kyverum, MigracionV1, Manual];
}

/// <summary>Origen de la aprobación de una identidad (columna <c>approval_origin</c>, HU #13283).</summary>
public static class BiometricApprovalOrigins
{
    public const string Automatica = "automatica";
    public const string Manual = "manual";
}

/// <summary>Estados de la máquina de biométrica.</summary>
public static class BiometricEstados
{
    public const string Enviado = "enviado";
    public const string EnProceso = "en_proceso";
    public const string Aprobado = "aprobado";
    public const string Rechazado = "rechazado";
    public const string Expirado = "expirado";

    /// <summary>
    /// El envío al proveedor externo falló de forma TRANSITORIA y quedó ENCOLADO para reintento por el
    /// worker (cola de envío, provider-agnostic). El worker lo pasa a <see cref="EnProceso"/> al lograr el
    /// envío, o a <see cref="ErrorEnvio"/> si agota los intentos.
    /// </summary>
    public const string PendienteEnvio = "pendiente_envio";

    /// <summary>El envío al proveedor agotó los reintentos (o falló de forma definitiva) → requiere acción.</summary>
    public const string ErrorEnvio = "error_envio";

    /// <summary>
    /// HU #13283 — el flujo manual está activo: Kyverum se canceló y la persona completa la captura por el
    /// enlace manual. NO es "en vuelo" de Kyverum (no lo cuentan las alertas de atascados ni los reintentos).
    /// </summary>
    public const string ManualActivo = "manual_activo";

    /// <summary>HU #13283 — la persona terminó la captura manual y espera la revisión humana.</summary>
    public const string PendienteRevisionManual = "pendiente_revision_manual";

    /// <summary>Todos los valores aceptados por <c>ck_biometric_validations_status</c>.</summary>
    public static readonly IReadOnlyList<string> Todos =
    [
        Enviado, EnProceso, Aprobado, Rechazado, Expirado, PendienteEnvio, ErrorEnvio, ManualActivo, PendienteRevisionManual,
    ];
}

/// <summary>
/// Estados de VIGENCIA (derivados) de una identidad aprobada, usados como filtro transversal del
/// submódulo de Validaciones. No es el estado persistido (<see cref="BiometricEstados"/>): se calcula a
/// partir de <c>ValidadoAt + VigenciaDias</c> contra la fecha actual.
/// </summary>
public static class BiometricVigenciaEstados
{
    /// <summary>Aprobada y dentro de los <see cref="BiometricRules.VigenciaDias"/> días de vigencia.</summary>
    public const string Vigente = "vigente";

    /// <summary>Vigente pero a <see cref="BiometricRules.VigenciaPorVencerDias"/> días o menos de vencer.</summary>
    public const string PorVencer = "por_vencer";

    /// <summary>Aprobada cuya vigencia ya se agotó (requiere revalidar).</summary>
    public const string Vencida = "vencida";
}

/// <summary>Reglas de negocio de la biométrica (compartidas Application/Domain).</summary>
public static class BiometricRules
{
    public const int MaxIntentos = 5;

    /// <summary>
    /// Intentos que Kyverum permite dentro de UNA validación antes de cerrarla rechazada. Kyverum NO expone
    /// este límite ni los "intentos restantes" en su API, así que se fija aquí (valor observado = 3): el
    /// reconciliador cuenta los intentos fallidos y solo marca <c>rechazado</c> al alcanzar este tope.
    /// </summary>
    public const int KyverumMaxIntentos = 3;

    /// <summary>
    /// Cuántas veces el worker de reconciliación sondea a Kyverum por VENTANA DE ACTIVIDAD (tras cada intento
    /// o (re)envío) antes de callar. Acota el sondeo de respaldo: en vez de consultar cada 2 min durante toda
    /// la vigencia del token (24 h), hace a lo sumo 3 consultas (~6 min) y espera una señal nueva (otro intento
    /// por webhook o un reconcile manual) para volver a sondear. Ver
    /// <see cref="ProcedureInstanceBiometricValidation.ReconcilePollCount"/>.
    /// </summary>
    public const int KyverumMaxReconcilePolls = 3;

    public const int ThresholdAprobacion = 60;
    public const int TokenTtlHoras = 24;

    public const string ParteComprador = "comprador";
    public const string ParteVendedor = "vendedor";

    /// <summary>HU #13246 — rol de la validación lanzada para un mandatario (con <c>MandateSignerId</c>).</summary>
    public const string ParteMandatario = "mandatario";

    /// <summary>
    /// Vigencia (días CALENDARIO) de una validación de identidad APROBADA, contada desde la fecha de
    /// aprobación (<c>ValidadoAt</c>). El día de aprobación es el día 1; vence en el día 31, es decir,
    /// el día <c>ValidadoAt + 30 días</c> ya NO es vigente. Pasada la vigencia hay que revalidar
    /// (HU #10350 — reuso de identidad vigente).
    /// </summary>
    public const int VigenciaDias = 30;

    /// <summary>
    /// Umbral (días calendario restantes) a partir del cual una identidad vigente se considera "por
    /// vencer" — alinea con el badge ámbar de la grilla y el filtro de vigencia (1..7 días).
    /// </summary>
    public const int VigenciaPorVencerDias = 7;

    /// <summary>
    /// ¿La validación está APROBADA y VIGENTE en la fecha <paramref name="now"/>? Vigente ⟺ el DÍA
    /// calendario de hoy (en hora de Colombia) es anterior a <c>ValidadoAt + VigenciaDias</c>: el día de
    /// aprobación es el día 1 y vence en el día 31. El corte es por DÍA, no por hora.
    /// Una aprobada sin <c>ValidadoAt</c> se trata como vigente: ese estado sólo ocurre en fixtures de
    /// prueba; en producción los tres caminos de aprobación (mock, simular, webhook Kyverum) siempre
    /// setean la fecha al aprobar.
    /// </summary>
    public static bool EsAprobadaVigente(ProcedureInstanceBiometricValidation validation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(validation);
        if (validation.Status != BiometricEstados.Aprobado)
            return false;
        // `valid_until` es la FUENTE DE VERDAD del vencimiento cuando está estampada: es editable en BD para
        // VENCER o EXTENDER una identidad, y la reutilización/gates respetan ese valor (antes se ignoraba y se
        // calculaba desde validated_at, por lo que editar valid_until no tenía efecto). Se estampa al aprobar
        // (= medianoche Colombia de validated_at + VigenciaDias). Si falta (fixtures/registros viejos), se cae
        // al cálculo por validated_at + VigenciaDias — mismo resultado que el valor estampado.
        if (validation.ValidUntil is { } validUntil)
            return now < validUntil;
        if (validation.ValidatedAt is not { } validadoAt)
            return true;
        // Día calendario en hora de Colombia (no UTC) para que coincida con el día del gestor.
        var hoy = now.ToOffset(ColombiaTime.Offset).Date;
        var diaAprobacion = validadoAt.ToOffset(ColombiaTime.Offset).Date;
        return hoy < diaAprobacion.AddDays(VigenciaDias);
    }

    /// <summary>
    /// Clave canónica de una IDENTIDAD por persona dentro de un tenant: <c>{tenant:N}|{TIPODOC}|{DOCUMENTO}</c>
    /// (mayúsculas, sin espacios). La identidad se valida UNA vez por persona y se referencia en N trámites
    /// hasta que venza (HU #10350, sin clonar); esta clave permite comparar aprobaciones vigentes por persona
    /// entre la capa de aplicación (gates) y la de datos (consulta en lote) sin divergir de formato.
    /// </summary>
    public static string IdentidadKey(Guid tenantId, string? tipoDoc, string? documento) =>
        $"{tenantId:N}|{(tipoDoc ?? string.Empty).Trim().ToUpperInvariant()}|{(documento ?? string.Empty).Trim().ToUpperInvariant()}";

    /// <summary>
    /// Fecha en que la validación deja de ser vigente: el DÍA calendario (Colombia) <c>ValidadoAt +
    /// VigenciaDias</c> (día de expiración, ya NO vigente — consistente con <see cref="EsAprobadaVigente"/>).
    /// <c>null</c> si la validación no tiene fecha de aprobación (<c>ValidadoAt</c>): no aplica vigencia.
    /// </summary>
    public static DateTimeOffset? FechaFinVigencia(ProcedureInstanceBiometricValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        return validation.ValidatedAt is { } validadoAt ? FechaFinVigencia(validadoAt) : null;
    }

    /// <summary>
    /// Fecha de fin de vigencia para una aprobación en <paramref name="validadoAt"/>: medianoche (hora
    /// Colombia) del día <c>validadoAt + VigenciaDias</c>. Es el valor que se ESTAMPA en <c>vigencia_hasta</c>
    /// al aprobar (<see cref="ProcedureInstanceBiometricValidation.Aprobar"/>).
    /// </summary>
    public static DateTimeOffset FechaFinVigencia(DateTimeOffset validadoAt)
    {
        var diaExpiracion = validadoAt.ToOffset(ColombiaTime.Offset).Date.AddDays(VigenciaDias);
        // El instante (medianoche Colombia) se conserva, pero se DEVUELVE en UTC (offset 0): Npgsql solo
        // acepta offset 0 al escribir en `timestamptz`; un offset -05:00 hacía fallar SaveChanges con
        // ArgumentException y devolvía 500 al aprobar (webhook y reconcile). Los lectores de vigencia
        // reconvierten con .ToOffset(ColombiaTime.Offset), así que el día calendario Colombia no cambia.
        return new DateTimeOffset(diaExpiracion, ColombiaTime.Offset).ToUniversalTime();
    }

    /// <summary>
    /// Días calendario (Colombia) que le restan de vigencia a una validación aprobada en la fecha
    /// <paramref name="now"/>: el día de aprobación reporta <see cref="VigenciaDias"/>, el último día
    /// vigente reporta 1 y el día de expiración (o posterior) reporta 0. <c>null</c> si no hay
    /// <c>ValidadoAt</c> (no aplica vigencia).
    /// </summary>
    public static int? DiasRestantesVigencia(ProcedureInstanceBiometricValidation validation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(validation);
        if (validation.ValidatedAt is not { } validadoAt)
            return null;
        var hoy = now.ToOffset(ColombiaTime.Offset).Date;
        var diaExpiracion = validadoAt.ToOffset(ColombiaTime.Offset).Date.AddDays(VigenciaDias);
        var dias = (diaExpiracion - hoy).Days;
        return dias < 0 ? 0 : dias;
    }

    /// <summary>
    /// ¿La validación corresponde al documento (tipo + número) de la parte ACTUAL del trámite? Defensa
    /// en profundidad del gate de identidad (HU #10350): aunque <c>EnsureIdentityHandler</c> expira las
    /// validaciones de una persona anterior cuando el gestor cambia el documento, el gate NO debe contar
    /// como aprobada una validación cuyo documento difiera del actor actual (p.ej. si esa invalidación no
    /// llegó a correr, falló de red o se saltó). El gate deja de depender de un mejor-esfuerzo del frontend.
    /// <para><b>Bug #13194 (P4, D4) — fail-closed.</b> Antes era «lenient»: si la validación o el sujeto
    /// no tenían número de documento, coincidía siempre. Eso dejaba aprobar a una persona DISTINTA del
    /// mismo tenant (una validación sin documento, o un sujeto sin documento, servía para cualquiera). Ahora
    /// sin los dos números no hay coincidencia: no se puede afirmar que la validación sea de este sujeto.
    /// Todos los caminos de creación vigentes (Kyverum, magic-link, prevalidación) exigen el documento, y la
    /// vigencia de una identidad es de 30 días, así que ninguna validación legítima vigente queda fuera.
    /// Los guardas de idempotencia (no iniciar una segunda validación) también usan esta regla, pero solo
    /// con 2+ actores en el rol: ahí una fila sin documento ya no bloquea al copropietario (con un solo
    /// actor el guarda no mira el documento y no cambia nada).
    /// El tipo de documento sigue descartando solo cuando ambos están presentes y difieren.</para>
    /// </summary>
    public static bool DocumentoCoincide(
        ProcedureInstanceBiometricValidation validation, string? tipoDoc, string? documento)
    {
        ArgumentNullException.ThrowIfNull(validation);
        if (string.IsNullOrWhiteSpace(documento) || string.IsNullOrWhiteSpace(validation.DocumentNumber))
            return false;
        if (!string.Equals(validation.DocumentNumber.Trim(), documento.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(tipoDoc) || string.IsNullOrWhiteSpace(validation.DocumentType))
            return true;
        return string.Equals(validation.DocumentType.Trim(), tipoDoc.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// HU #11751 (ADR-0050) — los CUATRO estados de vigencia que expone la consulta admin de identidad por
/// documento. No son los estados persistidos de <see cref="BiometricEstados"/>: son la clasificación
/// que ve un consumidor externo (área admin) que solo necesita saber si corresponde exigir una
/// validación nueva o no.
/// </summary>
public static class IdentityVigenciaEstados
{
    /// <summary>Ninguna validación para ese documento en el tenant (o la única que hay fue rechazada
    /// o falló su envío: ver nota de diseño en <see cref="IdentityVigenciaClassifier"/>).</summary>
    public const string SinValidacion = "sin_validacion";

    /// <summary>Hay una validación no terminal (enviada, en proceso, encolada de envío o en flujo manual).</summary>
    public const string EnCurso = "en_curso";

    /// <summary>Aprobada y dentro de la ventana de <see cref="BiometricRules.VigenciaDias"/>.</summary>
    public const string AprobadaVigente = "aprobada_vigente";

    /// <summary>Aprobada pero fuera de ventana, o expirada explícitamente.</summary>
    public const string Vencida = "vencida";
}

/// <summary>
/// HU #11751 (ADR-0050) — clasifica la validación MÁS RECIENTE de una persona en los cuatro estados de
/// <see cref="IdentityVigenciaEstados"/>. Es el ÚNICO punto de esta regla: lo consume el endpoint admin
/// de consulta por documento (<c>IdentityVigenciaPorDocumentoResolver</c>, capa Application) y
/// <c>MandateSignerDirectory</c> (HU #11752), para que ambos hablen el mismo idioma sobre "¿esta persona
/// tiene identidad vigente?" sin duplicar la regla de vigencia (que sigue viviendo en
/// <see cref="BiometricRules.EsAprobadaVigente"/>: esta clase no la reimplementa, solo la envuelve).
/// </summary>
public static class IdentityVigenciaClassifier
{
    /// <summary>
    /// Clasifica <paramref name="latest"/> (la validación más reciente del documento, o <c>null</c> si
    /// no hay ninguna) en uno de los cuatro estados.
    ///
    /// <para><b>Decisión de diseño — rechazada / error de envío:</b> se tratan igual que la AUSENCIA de
    /// validación (<see cref="IdentityVigenciaEstados.SinValidacion"/>), no como un quinto estado. La
    /// razón es que, desde el punto de vista del consumidor admin, ambos casos exigen exactamente la
    /// misma acción — iniciar una prevalidación nueva desde el módulo Identidad — y el ADR-0050 solo
    /// pide "exactamente cuatro estados". Distinguir "rechazada" complicaría el contrato sin cambiar la
    /// acción que el operador debe tomar.</para>
    /// </summary>
    public static string Classify(ProcedureInstanceBiometricValidation? latest, DateTimeOffset now)
    {
        if (latest is null)
            return IdentityVigenciaEstados.SinValidacion;

        return latest.Status switch
        {
            BiometricEstados.Aprobado => BiometricRules.EsAprobadaVigente(latest, now)
                ? IdentityVigenciaEstados.AprobadaVigente
                : IdentityVigenciaEstados.Vencida,
            BiometricEstados.Expirado => IdentityVigenciaEstados.Vencida,
            // HU #13283 — los estados manuales siguen en curso: nunca vigentes hasta que la revisión apruebe.
            BiometricEstados.Enviado or BiometricEstados.EnProceso or BiometricEstados.PendienteEnvio
                or BiometricEstados.ManualActivo or BiometricEstados.PendienteRevisionManual
                => IdentityVigenciaEstados.EnCurso,
            // Rechazado, error_envio o cualquier estado futuro no contemplado: sin_validacion (ver nota
            // de diseño arriba).
            _ => IdentityVigenciaEstados.SinValidacion,
        };
    }
}

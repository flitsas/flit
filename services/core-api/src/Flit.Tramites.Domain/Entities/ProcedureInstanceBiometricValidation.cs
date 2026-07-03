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
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>'comprador' | 'vendedor'. Null en matrícula inicial (única parte = comprador).</summary>
    public string? PartyRole { get; set; }

    public string Name { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>enviado | en_proceso | aprobado | rechazado | expirado.</summary>
    public string Status { get; set; } = BiometricEstados.Enviado;

    /// <summary>SHA-256 (hex) del token enviado por magic-link. El token crudo nunca se persiste.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }

    // ── Proveedor de validación de identidad (HU #10233 — Kyverum Verify) ────────

    /// <summary>'mock' | 'kyverum'. Default 'mock' (flujo determinista de 3 fotos). 'kyverum' = validación
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

    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = BiometricRules.MaxIntentos;

    /// <summary>
    /// <c>validadoAt</c> del último intento YA CONTADO en <see cref="Attempts"/> (Kyverum). Clave de dedup
    /// dedicada: cada intento fallido de Kyverum trae un <c>validadoAt</c> distinto y llega por webhook + poll +
    /// reenvíos; se cuenta UNA sola vez comparando contra este valor. Se escribe SOLO al contar un intento
    /// nuevo (nunca lo pisa un payload sin fecha), evitando el doble-conteo. Null hasta el primer intento fallido.
    /// </summary>
    public string? LastAttemptAt { get; set; }

    public int? Score { get; set; }
    public string? Detail { get; set; }

    public string? FacePhotoPath { get; set; }
    public string? IdFrontPhotoPath { get; set; }
    public string? IdBackPhotoPath { get; set; }

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

    public ProcedureInstance? ProcedureInstance { get; set; }

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

    public const int ThresholdAprobacion = 60;
    public const int TokenTtlHoras = 24;

    public const string ParteComprador = "comprador";
    public const string ParteVendedor = "vendedor";

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
    /// Huso horario de Colombia (UTC-5, sin horario de verano). La vigencia se cuenta por DÍA CALENDARIO
    /// local de Colombia —el día que ve el gestor— y no por el día UTC: una aprobación cerca de medianoche
    /// no debe contar un día de menos/más por la diferencia de 5 horas.
    /// </summary>
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

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
        var hoy = now.ToOffset(ColombiaUtcOffset).Date;
        var diaAprobacion = validadoAt.ToOffset(ColombiaUtcOffset).Date;
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
        var diaExpiracion = validadoAt.ToOffset(ColombiaUtcOffset).Date.AddDays(VigenciaDias);
        // El instante (medianoche Colombia) se conserva, pero se DEVUELVE en UTC (offset 0): Npgsql solo
        // acepta offset 0 al escribir en `timestamptz`; un offset -05:00 hacía fallar SaveChanges con
        // ArgumentException y devolvía 500 al aprobar (webhook y reconcile). Los lectores de vigencia
        // reconvierten con .ToOffset(ColombiaUtcOffset), así que el día calendario Colombia no cambia.
        return new DateTimeOffset(diaExpiracion, ColombiaUtcOffset).ToUniversalTime();
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
        var hoy = now.ToOffset(ColombiaUtcOffset).Date;
        var diaExpiracion = validadoAt.ToOffset(ColombiaUtcOffset).Date.AddDays(VigenciaDias);
        var dias = (diaExpiracion - hoy).Days;
        return dias < 0 ? 0 : dias;
    }

    /// <summary>
    /// ¿La validación corresponde al documento (tipo + número) de la parte ACTUAL del trámite? Defensa
    /// en profundidad del gate de identidad (HU #10350): aunque <c>EnsureIdentityHandler</c> expira las
    /// validaciones de una persona anterior cuando el gestor cambia el documento, el gate NO debe contar
    /// como aprobada una validación cuyo documento difiera del actor actual (p.ej. si esa invalidación no
    /// llegó a correr, falló de red o se saltó). El gate deja de depender de un mejor-esfuerzo del frontend.
    /// <para>Lenient: si la validación o el actor no tienen documento (fixtures/datos parciales), no se
    /// descarta por documento — sólo se descarta cuando AMBOS números están presentes y difieren. El tipo
    /// de documento sólo descarta cuando ambos están presentes y difieren.</para>
    /// </summary>
    public static bool DocumentoCoincide(
        ProcedureInstanceBiometricValidation validation, string? tipoDoc, string? documento)
    {
        ArgumentNullException.ThrowIfNull(validation);
        if (string.IsNullOrWhiteSpace(documento) || string.IsNullOrWhiteSpace(validation.DocumentNumber))
            return true;
        if (!string.Equals(validation.DocumentNumber.Trim(), documento.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(tipoDoc) || string.IsNullOrWhiteSpace(validation.DocumentType))
            return true;
        return string.Equals(validation.DocumentType.Trim(), tipoDoc.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

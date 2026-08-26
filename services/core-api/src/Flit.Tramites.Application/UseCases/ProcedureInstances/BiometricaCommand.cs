using System.Security.Cryptography;
using System.Text.Json;
using Flit.Tramites.Application.Biometrics;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs (contrato congelado, Slice 6) ──────────────────────────────────────

/// <summary>Vista interna (gestor autenticado) de una validación biométrica.</summary>
public sealed record BiometricValidationDto(
    Guid Id,
    string? PartyRole,
    string Name,
    string DocumentType,
    string DocumentNumber,
    string Email,
    string Status,
    int Intentos,
    int MaxIntentos,
    int? Score,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ValidatedAt,
    bool Expired,
    // HU #10233 (AC9): proveedor de la validación y URL de captura (solo kyverum + en_proceso).
    string Provider = BiometricProviders.Mock,
    string? CaptureUrl = null,
    // HU #10234 (AC4): motivo de rechazo SANITIZADO. Solo se expone en estado rechazado; null en otros.
    string? RejectionReason = null,
    // Motivo del ÚLTIMO intento fallido MIENTRAS la validación sigue abierta (en_proceso): Kyverum permite
    // reintentar (p.ej. "El rostro no es completamente visible…"). Null si no hubo intento fallido o no aplica.
    string? UltimoIntentoMotivo = null,
    // HU #11069 — trámite primario + otros trámites del tenant con la misma identidad (detalle VID).
    Guid? ProcedureInstanceId = null,
    string? ReferenceNumber = null,
    string? Modalidad = null,
    IReadOnlyList<LinkedProcedureDto>? LinkedProcedures = null,
    /// <summary>Fecha de registro de la validación (para ordenar historial más reciente → más antigua).</summary>
    DateTimeOffset? CreatedAt = null,
    /// <summary>Hash del certificado de firma digital (estampa), cuando la validación está aprobada.</summary>
    string? CertificateHash = null);

/// <summary>Resultado de iniciar: incluye el token CRUDO (solo aquí) para construir el magic-link.</summary>
public sealed record IniciarBiometriaResult(
    BiometricValidationDto Validation,
    string Token,
    string MagicLinkPath);

/// <summary>
/// Lista de validaciones + el proveedor biométrico CONFIGURADO (no por-validación): el frontend lo usa
/// para que el botón "Validar identidad" sea provider-aware (kyverum → validación real Kyverum; mock →
/// simular). Default mock por compatibilidad.
/// </summary>
public sealed record BiometricValidationsResponse(
    IReadOnlyList<BiometricValidationDto> Validations,
    string Provider = BiometricProviders.Mock,
    // HU #11014 (ADR-0025 §4) — partes cuya identidad queda cubierta por la FIRMA DEL BAÚL en vez de por
    // una validación biométrica (misma regla que el outcome `firma_baul` de EnsureIdentity, HU #10646).
    // La UI las rotula como "firmado desde el baúl" y no ofrece el certificado de identidad, que no existe.
    IReadOnlyList<string>? FirmaBaulPartes = null,
    // Bug #11615 — intentos de una parte que YA tiene identidad aprobada y vigente (propia o
    // referenciada de otro trámite): rechazados, expirados o en vuelo. Salen de `validations` para no
    // suplantar al estado vigente según la posición en que quedaran, pero siguen viajando aquí para
    // que el histórico esté disponible sin volver a consultar. Null cuando no hay ninguno.
    IReadOnlyList<BiometricValidationDto>? SupersededValidations = null,
    // HU #11665 — por qué NO se envió la validación de identidad a una parte jurídica. Derivado al
    // vuelo con la MISMA regla que usa el disparador (EnvioValidacionBloqueoRules), nunca persistido:
    // en cuanto el gestor corrige el dato, el motivo desaparece. Null cuando no hay ninguno.
    IReadOnlyList<EnvioValidacionMotivoDto>? MotivosNoEnvio = null);

/// <summary>
/// HU #11665 — motivo tipificado de no envío de la validación de identidad, por parte.
/// <c>informativo = true</c> no es un fallo: explica una ausencia legítima (la parte ya está cubierta)
/// y la UI no debe pintarlo como bloqueo. Es un derivado calculado del estado, igual que
/// <c>firmaBaulPartes</c>; no existe ninguna columna que lo guarde.
/// </summary>
public sealed record EnvioValidacionMotivoDto(string Parte, string Codigo, bool Informativo);

// NOTA: estos DOS contratos quedan en ESPAÑOL a propósito (request de iniciar + vista pública de
// captura). El renombrado a inglés (HU10350) cubre SOLO la tabla y sus respuestas (grilla/wizard/stuck);
// el front de iniciar/captura pública sigue enviando/leyendo estos campos en español.
/// <summary>Entrada para iniciar una biométrica de una parte.</summary>
public sealed record IniciarBiometriaInput(
    string? Parte,
    string Nombre,
    string TipoDoc,
    string Documento,
    string Email);

/// <summary>Vista PÚBLICA (participante vía magic-link): sin enumeración de PII sensible.</summary>
public sealed record BiometriaPublicViewDto(
    string Estado,
    string? Parte,
    string Nombre,
    int Intentos,
    int MaxIntentos,
    bool Expired);

/// <summary>Las 3 fotos como streams (igual que UploadAttachment).</summary>
public sealed record CompletarBiometriaInput(
    Stream? Rostro,
    Stream? CedulaFrontal,
    Stream? CedulaReverso);

public sealed record CompletarBiometriaResult(string Estado, int Score, string Motivo);

/// <summary>Helpers de token: genera token crudo (base64url 32 bytes) y su hash SHA-256.</summary>
public static class BiometricToken
{
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }
}

// ── Handler 1: Iniciar (autenticado) ────────────────────────────────────────

/// <summary>
/// Crea una validación biométrica para una parte de la instancia y devuelve el token CRUDO
/// (para construir el magic-link). Idempotente por parte: si ya existe una validación activa
/// (enviado/en_proceso) o aprobada para la misma parte, devuelve <c>biometria_activa</c> (409)
/// — el gestor debe reusar la existente en vez de duplicar. Requiere instancia en <c>draft</c>.
/// La parte se normaliza vía <c>NormalizeParte</c>: matrícula usa 'comprador' (la FE/iniciar lo
/// pasa explícito; vacío → null por compatibilidad legado); traspaso usa 'comprador'|'vendedor'.
/// HU #11265: capa PREVIA de precedencia de envío (persona/tenant) antes del guard por parte.
/// </summary>
public sealed class IniciarBiometriaHandler(
    IProcedureInstanceRepository repo,
    ISignatureVaultPolicy? vaultPolicy = null)
{
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

    public async Task<(IniciarBiometriaResult? Result, string? Error, IdentitySendDecision? Conflict)> HandleAsync(
        Guid id,
        Guid tenantId,
        IniciarBiometriaInput input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Nombre)
            || string.IsNullOrWhiteSpace(input.TipoDoc)
            || string.IsNullOrWhiteSpace(input.Documento)
            || string.IsNullOrWhiteSpace(input.Email))
            return (null, "datos_incompletos", null);

        var parte = NormalizeParte(input.Parte);
        if (parte is "invalid")
            return (null, "parte_invalida", null);

        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found", null);
        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            return (null, "not_draft", null);

        var tipoDoc = input.TipoDoc.Trim();
        var documento = input.Documento.Trim();
        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase));
        var now = DateTimeOffset.UtcNow;

        // HU #11265 — precedencia de envío (capa PREVIA a idempotencia por parte).
        var decision = await IdentitySendDecisionForTramite.EvaluateAsync(
            repo, _vaultPolicy, tenantId, actor, tipoDoc, documento,
            instance.BiometricValidations.ToList(), now, ct);
        var (sendError, sendConflict, continueStart) = IdentitySendDecisionForTramite.ToStartOutcome(
            decision, id, parte, instance.BiometricValidations.ToList());
        if (!continueStart)
            return (null, sendError, sendConflict);

        // Idempotencia por parte: una validación activa o aprobada bloquea recrear.
        var existing = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
            && v.Status is BiometricEstados.Enviado or BiometricEstados.EnProceso or BiometricEstados.Aprobado);
        if (existing is not null)
            return (null, "biometria_activa", null);

        var token = BiometricToken.Generate();
        var validation = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            PartyRole = parte,
            Name = input.Nombre.Trim(),
            DocumentType = tipoDoc,
            DocumentNumber = documento,
            Email = input.Email.Trim(),
            Status = BiometricEstados.Enviado,
            TokenHash = BiometricToken.Hash(token),
            ExpiresAt = now.AddHours(BiometricRules.TokenTtlHoras),
            Attempts = 0,
            MaxAttempts = BiometricRules.MaxIntentos,
            CreatedAt = now,
        };

        instance.BiometricValidations.Add(validation);
        // PK store-generated con Id ya seteado: marcar Added explícito para forzar INSERT
        // (mismo bug/convención que UploadAttachmentHandler).
        repo.Add(validation);
        try
        {
            await repo.SaveChangesAsync(ct);
        }
        catch (Domain.Identity.IdentityInFlightConflictException)
        {
            // HU #11266 — carrera: otra petición ya creó la fila en vuelo.
            return (null, "biometria_activa",
                IdentitySendDecisionForTramite.InFlightRaceConflict(IdentitySendOrigen.Tramite));
        }

        var dto = ToDto(validation, now);
        var result = new IniciarBiometriaResult(dto, token, $"/biometric/{token}");
        return (result, null, null);
    }

    private static string? NormalizeParte(string? parte)
    {
        if (string.IsNullOrWhiteSpace(parte))
            return null;
        var p = parte.Trim().ToLowerInvariant();
        return p is BiometricRules.ParteComprador or BiometricRules.ParteVendedor ? p : "invalid";
    }

    internal static BiometricValidationDto ToDto(ProcedureInstanceBiometricValidation v, DateTimeOffset now) =>
        new(v.Id, v.PartyRole, v.Name, v.DocumentType, v.DocumentNumber, v.Email, v.Status,
            v.Attempts, v.MaxAttempts, v.Score, v.ExpiresAt, v.ValidatedAt,
            v.Status != BiometricEstados.Aprobado && now > v.ExpiresAt,
            v.Provider,
            // AC9: la URL de captura solo se expone para Kyverum mientras la validación está en proceso.
            string.Equals(v.Provider, BiometricProviders.Kyverum, StringComparison.OrdinalIgnoreCase)
                && v.Status == BiometricEstados.EnProceso
                    ? v.CaptureUrl
                    : null,
            ExtractMotivoRechazo(v),
            ExtractUltimoIntentoMotivo(v),
            CreatedAt: v.CreatedAt,
            CertificateHash: v.CertificateHash);

    /// <summary>
    /// Motivo de rechazo SANITIZADO para mostrar al gestor (HU #10234 AC4). Solo se expone en estado
    /// rechazado; en cualquier otro estado es null. NUNCA incluye PII cruda: los JSON de origen
    /// (Detalle del scorer mock, ProviderPayload de Kyverum) ya están sanitizados. Para mock se toma
    /// `motivo`; para Kyverum se deriva un texto amigable de las coincidencias reportadas.
    /// </summary>
    internal static string? ExtractMotivoRechazo(ProcedureInstanceBiometricValidation v)
    {
        if (v.Status != BiometricEstados.Rechazado)
            return null;

        // Mock: Detail = { score, aprobado, motivo, ... } — el scorer ya produce texto sin PII.
        var motivo = TryReadString(v.Detail, "motivo");
        if (!string.IsNullOrWhiteSpace(motivo))
            return motivo;

        // Kyverum: mensaje REAL del proveedor (subject_motivo), capturado en la consulta de estado. Es una guía
        // amigable sin PII (p.ej. "coincidencia facial insuficiente"), mejor que el texto genérico de abajo.
        var kyverumMotivo = TryReadString(v.ProviderPayload, "subject_motivo");
        if (!string.IsNullOrWhiteSpace(kyverumMotivo))
            return kyverumMotivo;

        // Kyverum: deriva el motivo de las coincidencias sanitizadas del payload del proveedor.
        if (TryParseJson(v.ProviderPayload, out var payload)
            && payload.TryGetProperty("coincidencias", out var coincidencias)
            && coincidencias.ValueKind == JsonValueKind.Object)
        {
            if (coincidencias.TryGetProperty("documento", out var doc) && doc.ValueKind == JsonValueKind.False)
                return "La verificación del documento no fue exitosa.";
            if (coincidencias.TryGetProperty("nombre", out var nombre) && nombre.ValueKind == JsonValueKind.False)
                return "Los datos personales no coinciden con el documento.";
        }

        return "La validación de identidad no fue aprobada por el proveedor.";
    }

    /// <summary>
    /// Motivo del ÚLTIMO intento fallido mientras la validación sigue ABIERTA (en_proceso/enviado): Kyverum
    /// permite reintentar (hasta agotar sus intentos). Se lee del provider_payload sanitizado (subject_motivo,
    /// estampado por la consulta de estado) SOLO si el último intento fue rechazado. Null si no aplica. Permite
    /// al front mostrar "el cliente está reintentando: &lt;motivo&gt;" sin terminalizar la validación.
    /// </summary>
    internal static string? ExtractUltimoIntentoMotivo(ProcedureInstanceBiometricValidation v)
    {
        if (v.Status is not BiometricEstados.EnProceso and not BiometricEstados.Enviado)
            return null;
        if (!TryParseJson(v.ProviderPayload, out var payload))
            return null;
        if (!(payload.TryGetProperty("subject_status", out var st)
              && st.ValueKind == JsonValueKind.String
              && string.Equals(st.GetString(), BiometricEstados.Rechazado, StringComparison.OrdinalIgnoreCase)))
            return null;
        return payload.TryGetProperty("subject_motivo", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : null;
    }

    private static string? TryReadString(string? json, string property)
    {
        if (!TryParseJson(json, out var root))
            return null;
        return root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryParseJson(string? json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

// ── Handler: listar (autenticado) ───────────────────────────────────────────

/// <summary>Lista las validaciones biométricas de una instancia (vista del gestor).</summary>
public sealed class ListBiometriaHandler(
    IProcedureInstanceRepository repo,
    BiometricsProviderOptions providerOptions,
    ISignatureVaultPolicy? vaultPolicy = null)
{
    // ADR-0025 §4 — sin baúl inyectado (tests que no lo ejercitan) NUNCA hay cobertura de firma.
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;


    public async Task<(BiometricValidationsResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        var dtos = instance.BiometricValidations
            .OrderBy(v => v.CreatedAt)
            .Select(v => IniciarBiometriaHandler.ToDto(v, now))
            .ToList();

        // Identidad REFERENCIADA (HU #10350): si el trámite no tiene validación PROPIA vigente-aprobada de una
        // parte pero la PERSONA (documento del actor) sí tiene una en otro trámite, se expone ESA (sin clonar)
        // para que la UI muestre "identidad verificada". La validación referenciada se rotula con la parte
        // actual (su PartyRole de origen puede diferir, p.ej. matrícula→traspaso).
        var esTraspaso = instance.Family
                         == ProcedureFamily.Traspaso;
        var partes = esTraspaso ? new[] { "comprador", "vendedor" } : new[] { "comprador" };
        var firmaBaulPartes = new List<string>(partes.Length);
        // Bug #11615 — identidad que PREVALECE por parte (aprobada + vigente, propia o referenciada) y
        // resto de entradas aprobadas+vigentes de esa parte. Con esto el listado deja de depender de en
        // qué posición cayó cada intento. Ver BiometricListPrevalence.
        var prevalecientePorParte = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var vigentesPorParte = new Dictionary<string, IReadOnlySet<Guid>>(StringComparer.OrdinalIgnoreCase);
        // Documento del sujeto de identidad de cada parte: sin él, el emparejamiento de las filas SIN
        // ROL (matrícula) le atribuiría a la parte intentos de otra persona y los sacaría de la vista.
        var documentoPorParte = new Dictionary<string, ParteDocumento>(StringComparer.OrdinalIgnoreCase);
        // HU #11665 — motivo tipificado de no envío por parte. `partesJuridicas` acota la segunda
        // pasada (los motivos informativos): una persona natural no reporta motivos.
        var motivosNoEnvio = new List<EnvioValidacionMotivoDto>(partes.Length);
        var partesJuridicas = new List<string>(partes.Length);
        foreach (var parte in partes)
        {
            var actor = instance.Actors.FirstOrDefault(a =>
                string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase));
            if (actor is null)
                continue;

            // Documento del SUJETO de identidad (HU #10688): el RL en PJ, el actor en PN.
            var subject = IdentitySubjectResolver.For(actor);

            // HU #11665 — el motivo lo calcula la MISMA regla que usa el disparador al escribir
            // (EnvioValidacionBloqueoRules): una sola fuente, así el listado no puede explicar el no
            // envío de una forma distinta a como se decidió. Se registra ANTES de los `continue` de
            // abajo, porque el caso «RL sin documento» es justamente uno de los que cortan aquí.
            var estadoEnvio = EnvioValidacionBloqueoRules.EstadoDe(actor, subject, providerOptions.IsKyverum);
            if (estadoEnvio.ActorEsJuridico)
            {
                partesJuridicas.Add(parte);
                var motivoDatos = EnvioValidacionBloqueoRules.Evaluar(estadoEnvio);
                if (motivoDatos is not null)
                    motivosNoEnvio.Add(new EnvioValidacionMotivoDto(parte, motivoDatos.Codigo, motivoDatos.Informativo));
            }

            if (string.IsNullOrWhiteSpace(subject.TipoDocumento) || string.IsNullOrWhiteSpace(subject.NumeroDocumento))
                continue;

            documentoPorParte[parte] = new ParteDocumento(subject.TipoDocumento, subject.NumeroDocumento);

            // HU #11014 — cobertura por FIRMA DEL BAÚL: la identidad del sujeto queda satisfecha por su
            // firma vigente y NO hay validación biométrica ni certificado que mostrar.
            //
            // Bug #11141 — antes bastaba con que EXISTIERA una firma vigente, sin mirar el mecanismo
            // elegido por el gestor ni si el actor era jurídico. Con "validación de identidad"
            // seleccionada, el documento se firmaba con el sello de identidad (correcto) pero esta
            // lista rotulaba a la parte como firmada desde el baúl, y de ahí salían el resumen del paso
            // FUR y las pestañas de comprador/vendedor del expediente. Ahora la condición es el mismo
            // predicado que usa el generador, así que la vista no puede contradecir al documento.
            if (FirmaBaulCobertura.Aplica(actor))
            {
                var firmaBaul = await _vaultPolicy
                    .ResolveAsync(instance.TenantId, subject.TipoDocumento.Trim(), subject.NumeroDocumento.Trim(), ct)
                    .ConfigureAwait(false);
                if (firmaBaul is not null)
                    firmaBaulPartes.Add(parte);
            }

            // Filas PROPIAS de la parte que ya están aprobadas y vigentes para el documento del sujeto:
            // son las únicas que pueden representar el estado de la parte sin referenciar otro trámite.
            var vigentesLocales = instance.BiometricValidations
                .Where(v => string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
                            && BiometricRules.EsAprobadaVigente(v, now)
                            && BiometricRules.DocumentoCoincide(v, subject.TipoDocumento, subject.NumeroDocumento))
                .ToList();
            if (vigentesLocales.Count > 0)
            {
                vigentesPorParte[parte] = vigentesLocales.Select(v => v.Id).ToHashSet();
                // La más reciente manda: si hubo varias aprobaciones vigentes, la última aprobación es la
                // que el gestor considera actual.
                prevalecientePorParte[parte] = vigentesLocales
                    .OrderByDescending(v => v.ValidatedAt ?? v.CreatedAt)
                    .First().Id;
                continue;
            }

            var source = await repo.FindVigenteApprovedByDocumentAsync(
                instance.TenantId, subject.TipoDocumento.Trim(), subject.NumeroDocumento.Trim(), now, ct);
            if (source is null)
                continue;

            // La identidad referenciada solo se agrega si no está ya en el listado: la consulta busca por
            // documento en TODO el tenant y puede devolver una fila de este mismo trámite (p.ej. rotulada
            // con otro rol o sin rol).
            //
            // Cuando ya está, NO basta con marcarla como prevaleciente: los consumidores emparejan POR
            // ROL, así que una fila de este trámite rotulada con el otro rol subía a la primera posición
            // pero seguía sin representar a esta parte, y la parte se veía sin identidad vigente. Se
            // re-rotula con la parte actual (mismo criterio que la fila que se agrega desde otro
            // trámite), salvo que la etiqueta que trae sea LEGÍTIMA: si la otra parte del traspaso tiene
            // ese mismo documento, la fila es suya y robársela dejaría a esa otra parte sin identidad.
            var indiceExistente = dtos.FindIndex(d => d.Id == source.Id);
            if (indiceExistente < 0)
            {
                dtos.Add(IniciarBiometriaHandler.ToDto(source, now) with { PartyRole = parte });
            }
            else if (!string.Equals(dtos[indiceExistente].PartyRole, parte, StringComparison.OrdinalIgnoreCase)
                     && !EsDeOtraParte(dtos[indiceExistente], parte, instance, partes))
            {
                dtos[indiceExistente] = dtos[indiceExistente] with { PartyRole = parte };
            }

            prevalecientePorParte[parte] = source.Id;
            vigentesPorParte[parte] = new HashSet<Guid> { source.Id };
        }

        // HU #11665 — motivos INFORMATIVOS: la parte jurídica está completa, pero no se le envía nada
        // porque ya está cubierta. Se derivan de lo que este handler YA resolvió —la cobertura del baúl
        // y la identidad aprobada vigente (propia o referenciada)—, que son los pasos 1 y 2 de la
        // precedencia que evalúa el disparador río abajo. Cero consultas nuevas.
        foreach (var parte in partesJuridicas)
        {
            if (motivosNoEnvio.Exists(m => string.Equals(m.Parte, parte, StringComparison.OrdinalIgnoreCase)))
                continue; // Ya hay un motivo de datos: ese manda, es el que el gestor debe corregir.

            var informativo = EnvioValidacionBloqueoRules.DesdeCobertura(
                firmaBaulPartes.Contains(parte, StringComparer.OrdinalIgnoreCase),
                prevalecientePorParte.ContainsKey(parte));
            if (informativo is not null)
                motivosNoEnvio.Add(new EnvioValidacionMotivoDto(parte, informativo.Codigo, informativo.Informativo));
        }

        // Bug #11615 — la entrada aprobada y vigente de cada parte prevalece sobre sus intentos
        // rechazados / expirados / en vuelo, que pasan a `supersededValidations`.
        var (validations, superseded) = BiometricListPrevalence.Apply(
            dtos, prevalecientePorParte, vigentesPorParte, documentoPorParte, esTraspaso);

        return (new BiometricValidationsResponse(
            validations,
            providerOptions.Provider,
            firmaBaulPartes,
            superseded.Count > 0 ? superseded : null,
            motivosNoEnvio.Count > 0 ? motivosNoEnvio : null), null);
    }

    /// <summary>
    /// ¿La etiqueta de rol que ya trae la entrada pertenece legítimamente a OTRA parte del trámite? Lo
    /// es cuando el sujeto de identidad de esa otra parte tiene el mismo documento que la entrada (dos
    /// partes con el mismo documento). En ese caso la fila no se re-rotula: hacerlo dejaría sin
    /// identidad vigente a la parte que sí la tenía.
    /// </summary>
    private static bool EsDeOtraParte(
        BiometricValidationDto dto, string parte, ProcedureInstance instance, IReadOnlyList<string> partes)
    {
        if (dto.PartyRole is null)
            return false;

        var otra = partes.FirstOrDefault(p =>
            !string.Equals(p, parte, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p, dto.PartyRole, StringComparison.OrdinalIgnoreCase));
        if (otra is null)
            return false;

        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, otra, StringComparison.OrdinalIgnoreCase));
        if (actor is null)
            return false;

        var subject = IdentitySubjectResolver.For(actor);
        return !string.IsNullOrWhiteSpace(subject.NumeroDocumento)
            && !string.IsNullOrWhiteSpace(dto.DocumentNumber)
            && string.Equals(subject.NumeroDocumento.Trim(), dto.DocumentNumber.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

// ── Handler 2: info por token (público) ─────────────────────────────────────

/// <summary>
/// Vista pública por token (magic-link). No filtra existencia: token desconocido → not_found
/// genérico. Marca y persiste <c>expirado</c> si pasó <c>expires_at</c> y aún no estaba resuelta.
/// </summary>
public sealed class GetBiometriaByTokenHandler(IProcedureInstanceRepository repo)
{
    public async Task<(BiometriaPublicViewDto? Result, string? Error)> HandleAsync(
        string token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, "not_found");

        var v = await repo.GetBiometricByTokenHashAsync(BiometricToken.Hash(token), ct);
        if (v is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        if (IsExpirable(v) && now > v.ExpiresAt)
        {
            v.Status = BiometricEstados.Expirado;
            v.UpdatedAt = now;
            await repo.SaveChangesAsync(ct);
        }

        var expired = v.Status == BiometricEstados.Expirado
            || (IsExpirable(v) && now > v.ExpiresAt);
        return (new BiometriaPublicViewDto(v.Status, v.PartyRole, v.Name, v.Attempts, v.MaxAttempts, expired), null);
    }

    internal static bool IsExpirable(ProcedureInstanceBiometricValidation v) =>
        v.Status is BiometricEstados.Enviado or BiometricEstados.EnProceso;
}

// ── Handler 3: completar (público) ──────────────────────────────────────────

/// <summary>
/// Completa la biométrica con las 3 fotos (público vía magic-link). Guard atómico: rechaza si
/// la validación está expirada, agotó intentos (>= max) o no está en estado (enviado|rechazado|en_proceso).
/// Pasa a en_proceso, almacena las fotos, corre el scorer MOCK y resuelve a aprobado/rechazado
/// con score/detalle/validado_at. Incrementa intentos en CADA intento.
/// </summary>
public sealed class CompletarBiometriaHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage,
    IBiometricScorer scorer)
{
    public async Task<(CompletarBiometriaResult? Result, string? Error)> HandleAsync(
        string token,
        CompletarBiometriaInput input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, "not_found");

        var v = await repo.GetBiometricByTokenHashAsync(BiometricToken.Hash(token), ct);
        if (v is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;

        // Expiración: marca expirado y rechaza el intento.
        if (GetBiometriaByTokenHandler.IsExpirable(v) && now > v.ExpiresAt)
        {
            v.Status = BiometricEstados.Expirado;
            v.UpdatedAt = now;
            await repo.SaveChangesAsync(ct);
            return (null, "expirada");
        }

        // Solo se puede (re)intentar desde enviado/rechazado/en_proceso; aprobado/expirado son terminales.
        if (v.Status is BiometricEstados.Aprobado or BiometricEstados.Expirado)
            return (null, "estado_invalido");

        if (v.Attempts >= v.MaxAttempts)
            return (null, "intentos_agotados");

        // Lee las 3 fotos a memoria (el scorer es mock; el almacenamiento persiste el binario).
        var rostro = await ReadAsync(input.Rostro, ct);
        var frontal = await ReadAsync(input.CedulaFrontal, ct);
        var reverso = await ReadAsync(input.CedulaReverso, ct);

        v.Status = BiometricEstados.EnProceso;
        v.Attempts += 1;
        v.UpdatedAt = now;

        // Persiste las fotos presentes (reusa IAttachmentStorage con tipos biometric_*).
        // El scorer mock solo se usa en validaciones ligadas a una instancia de trámite (HU #10865).
        var instanceIdForStorage = v.ProcedureInstanceId.GetValueOrDefault();
        if (rostro is { Length: > 0 })
            v.FacePhotoPath = (await storage.SaveAsync(instanceIdForStorage, "biometric_rostro", "rostro", new MemoryStream(rostro), ct)).StoragePath;
        if (frontal is { Length: > 0 })
            v.IdFrontPhotoPath = (await storage.SaveAsync(instanceIdForStorage, "biometric_cedula_frontal", "cedula_frontal", new MemoryStream(frontal), ct)).StoragePath;
        if (reverso is { Length: > 0 })
            v.IdBackPhotoPath = (await storage.SaveAsync(instanceIdForStorage, "biometric_cedula_reverso", "cedula_reverso", new MemoryStream(reverso), ct)).StoragePath;

        var score = scorer.Score(new BiometricPhotos(rostro, frontal, reverso));

        v.Score = score.Score;
        v.Detail = JsonSerializer.Serialize(new
        {
            score = score.Score,
            aprobado = score.Aprobado,
            motivo = score.Motivo,
            scorer = "mock",
            evaluado_at = now,
        });

        if (score.Aprobado)
        {
            v.Approve(now); // estado + validated_at + estampa valid_until
        }
        else
        {
            v.Status = BiometricEstados.Rechazado;
        }

        await repo.SaveChangesAsync(ct);
        return (new CompletarBiometriaResult(v.Status, score.Score, score.Motivo), null);
    }

    private static async Task<byte[]?> ReadAsync(Stream? stream, CancellationToken ct)
    {
        if (stream is null)
            return null;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}

// ── Handler: simular biométrica (autenticado, mock — sin fotos) ───────────────

/// <summary>
/// Aprueba directamente la biométrica de una parte (acción MOCK que sustituye el flujo de captura de
/// 3 fotos por un botón "Simular validación"). Mismo patrón que <see cref="SimularFirmaHandler"/>.
/// <para><paramref name="parte"/> vacía → <c>"comprador"</c> (matrícula, única parte); debe ser
/// <c>"comprador"</c>|<c>"vendedor"</c> (else <c>parte_invalida</c>). Resuelve el actor de esa parte
/// (comprador→BUYER/"comprador", vendedor→OWNER/"vendedor"); si falta → <c>actor_requerido</c>.</para>
/// Idempotente: si ya hay una validación <c>aprobado</c> para la parte, la devuelve sin cambios. Si no,
/// crea/actualiza la validación con estado <c>aprobado</c>, score 95, proveedor/scorer "mock" y los
/// datos del actor. Devuelve el mismo <see cref="BiometricValidationDto"/> que la lista.
/// </summary>
public sealed class SimularBiometriaHandler(IProcedureInstanceRepository repo)
{
    private const int MockScore = 95;

    public async Task<(BiometricValidationDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        string? parte,
        CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(parte)
            ? BiometricRules.ParteComprador
            : parte.Trim().ToLowerInvariant();
        if (normalized is not (BiometricRules.ParteComprador or BiometricRules.ParteVendedor))
            return (null, "parte_invalida");

        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "instance_not_found");

        var now = DateTimeOffset.UtcNow;

        // Idempotencia por parte: una validación ya aprobada se devuelve intacta.
        var existing = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.PartyRole, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is { Status: BiometricEstados.Aprobado })
            return (IniciarBiometriaHandler.ToDto(existing, now), null);

        // Actor de la parte (ActorType guarda el rol: "comprador"/"vendedor").
        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, normalized, StringComparison.OrdinalIgnoreCase));
        if (actor is null)
            return (null, "actor_requerido");

        // Sujeto de identidad (HU #10688): natural → actor; jurídica → representante legal. La validación se
        // ancla al documento/correo del sujeto para que la PJ quede firmada por el RL, no por el NIT.
        var subject = IdentitySubjectResolver.For(actor);

        var detalle = JsonSerializer.Serialize(new
        {
            score = MockScore,
            aprobado = true,
            motivo = "simulada",
            scorer = "mock",
            proveedor = "mock",
            evaluado_at = now,
        });

        ProcedureInstanceBiometricValidation validation;
        if (existing is not null)
        {
            // Reusa la validación existente (p.ej. enviado/rechazado): pásala a aprobado.
            validation = existing;
            validation.Score = MockScore;
            validation.Detail = detalle;
            validation.Name = subject.Nombre ?? actor.FullName;
            validation.DocumentType = subject.TipoDocumento ?? actor.DocumentType;
            validation.DocumentNumber = subject.NumeroDocumento ?? actor.DocumentNumber;
            validation.Email = subject.Email ?? string.Empty;
            validation.Approve(now); // estado + validated_at + estampa valid_until + updated_at
        }
        else
        {
            validation = new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                PartyRole = normalized,
                Name = subject.Nombre ?? actor.FullName,
                DocumentType = subject.TipoDocumento ?? actor.DocumentType,
                DocumentNumber = subject.NumeroDocumento ?? actor.DocumentNumber,
                Email = subject.Email ?? string.Empty,
                Status = BiometricEstados.Aprobado,
                TokenHash = BiometricToken.Hash(BiometricToken.Generate()),
                ExpiresAt = now.AddHours(BiometricRules.TokenTtlHoras),
                Attempts = 0,
                MaxAttempts = BiometricRules.MaxIntentos,
                Score = MockScore,
                Detail = detalle,
                ValidatedAt = now,
                ValidUntil = BiometricRules.FechaFinVigencia(now), // estampa el fin de vigencia
                CreatedAt = now,
            };
            instance.BiometricValidations.Add(validation);
            // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar INSERT.
            repo.Add(validation);
        }

        await repo.SaveChangesAsync(ct);
        return (IniciarBiometriaHandler.ToDto(validation, now), null);
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using Flit.Tramites.Application.Biometrics;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs (contrato congelado, Slice 6) ──────────────────────────────────────

/// <summary>Vista interna (gestor autenticado) de una validación biométrica.</summary>
public sealed record BiometricValidationDto(
    Guid Id,
    string? Parte,
    string Nombre,
    string TipoDoc,
    string Documento,
    string Email,
    string Estado,
    int Intentos,
    int MaxIntentos,
    int? Score,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ValidadoAt,
    bool Expired);

/// <summary>Resultado de iniciar: incluye el token CRUDO (solo aquí) para construir el magic-link.</summary>
public sealed record IniciarBiometriaResult(
    BiometricValidationDto Validation,
    string Token,
    string MagicLinkPath);

public sealed record BiometricValidationsResponse(IReadOnlyList<BiometricValidationDto> Validations);

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
/// </summary>
public sealed class IniciarBiometriaHandler(IProcedureInstanceRepository repo)
{
    public async Task<(IniciarBiometriaResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        IniciarBiometriaInput input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Nombre)
            || string.IsNullOrWhiteSpace(input.TipoDoc)
            || string.IsNullOrWhiteSpace(input.Documento)
            || string.IsNullOrWhiteSpace(input.Email))
            return (null, "datos_incompletos");

        var parte = NormalizeParte(input.Parte);
        if (parte is "invalid")
            return (null, "parte_invalida");

        var instance = await repo.GetByIdWithBiometricsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");
        if (instance.Status != ProcedureInstanceStatus.Draft)
            return (null, "not_draft");

        // Idempotencia por parte: una validación activa o aprobada bloquea recrear.
        var existing = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.Parte, parte, StringComparison.OrdinalIgnoreCase)
            && v.Estado is BiometricEstados.Enviado or BiometricEstados.EnProceso or BiometricEstados.Aprobado);
        if (existing is not null)
            return (null, "biometria_activa");

        var now = DateTimeOffset.UtcNow;
        var token = BiometricToken.Generate();
        var validation = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Parte = parte,
            Nombre = input.Nombre.Trim(),
            TipoDoc = input.TipoDoc.Trim(),
            Documento = input.Documento.Trim(),
            Email = input.Email.Trim(),
            Estado = BiometricEstados.Enviado,
            TokenHash = BiometricToken.Hash(token),
            ExpiresAt = now.AddHours(BiometricRules.TokenTtlHoras),
            Intentos = 0,
            MaxIntentos = BiometricRules.MaxIntentos,
            CreatedAt = now,
        };

        instance.BiometricValidations.Add(validation);
        // PK store-generated con Id ya seteado: marcar Added explícito para forzar INSERT
        // (mismo bug/convención que UploadAttachmentHandler).
        repo.Add(validation);
        await repo.SaveChangesAsync(ct);

        var dto = ToDto(validation, now);
        var result = new IniciarBiometriaResult(dto, token, $"/biometric/{token}");
        return (result, null);
    }

    private static string? NormalizeParte(string? parte)
    {
        if (string.IsNullOrWhiteSpace(parte))
            return null;
        var p = parte.Trim().ToLowerInvariant();
        return p is BiometricRules.ParteComprador or BiometricRules.ParteVendedor ? p : "invalid";
    }

    internal static BiometricValidationDto ToDto(ProcedureInstanceBiometricValidation v, DateTimeOffset now) =>
        new(v.Id, v.Parte, v.Nombre, v.TipoDoc, v.Documento, v.Email, v.Estado,
            v.Intentos, v.MaxIntentos, v.Score, v.ExpiresAt, v.ValidadoAt,
            v.Estado != BiometricEstados.Aprobado && now > v.ExpiresAt);
}

// ── Handler: listar (autenticado) ───────────────────────────────────────────

/// <summary>Lista las validaciones biométricas de una instancia (vista del gestor).</summary>
public sealed class ListBiometriaHandler(IProcedureInstanceRepository repo)
{
    public async Task<(BiometricValidationsResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithBiometricsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        var dtos = instance.BiometricValidations
            .OrderBy(v => v.CreatedAt)
            .Select(v => IniciarBiometriaHandler.ToDto(v, now))
            .ToList();
        return (new BiometricValidationsResponse(dtos), null);
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
            v.Estado = BiometricEstados.Expirado;
            v.UpdatedAt = now;
            await repo.SaveChangesAsync(ct);
        }

        var expired = v.Estado == BiometricEstados.Expirado
            || (IsExpirable(v) && now > v.ExpiresAt);
        return (new BiometriaPublicViewDto(v.Estado, v.Parte, v.Nombre, v.Intentos, v.MaxIntentos, expired), null);
    }

    internal static bool IsExpirable(ProcedureInstanceBiometricValidation v) =>
        v.Estado is BiometricEstados.Enviado or BiometricEstados.EnProceso;
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
            v.Estado = BiometricEstados.Expirado;
            v.UpdatedAt = now;
            await repo.SaveChangesAsync(ct);
            return (null, "expirada");
        }

        // Solo se puede (re)intentar desde enviado/rechazado/en_proceso; aprobado/expirado son terminales.
        if (v.Estado is BiometricEstados.Aprobado or BiometricEstados.Expirado)
            return (null, "estado_invalido");

        if (v.Intentos >= v.MaxIntentos)
            return (null, "intentos_agotados");

        // Lee las 3 fotos a memoria (el scorer es mock; el almacenamiento persiste el binario).
        var rostro = await ReadAsync(input.Rostro, ct);
        var frontal = await ReadAsync(input.CedulaFrontal, ct);
        var reverso = await ReadAsync(input.CedulaReverso, ct);

        v.Estado = BiometricEstados.EnProceso;
        v.Intentos += 1;
        v.UpdatedAt = now;

        // Persiste las fotos presentes (reusa IAttachmentStorage con tipos biometric_*).
        if (rostro is { Length: > 0 })
            v.FotoRostroPath = (await storage.SaveAsync(v.ProcedureInstanceId, "biometric_rostro", "rostro", new MemoryStream(rostro), ct)).StoragePath;
        if (frontal is { Length: > 0 })
            v.FotoCedulaFrontalPath = (await storage.SaveAsync(v.ProcedureInstanceId, "biometric_cedula_frontal", "cedula_frontal", new MemoryStream(frontal), ct)).StoragePath;
        if (reverso is { Length: > 0 })
            v.FotoCedulaReversoPath = (await storage.SaveAsync(v.ProcedureInstanceId, "biometric_cedula_reverso", "cedula_reverso", new MemoryStream(reverso), ct)).StoragePath;

        var score = scorer.Score(new BiometricPhotos(rostro, frontal, reverso));

        v.Score = score.Score;
        v.Detalle = JsonSerializer.Serialize(new
        {
            score = score.Score,
            aprobado = score.Aprobado,
            motivo = score.Motivo,
            scorer = "mock",
            evaluado_at = now,
        });

        if (score.Aprobado)
        {
            v.Estado = BiometricEstados.Aprobado;
            v.ValidadoAt = now;
        }
        else
        {
            v.Estado = BiometricEstados.Rechazado;
        }

        await repo.SaveChangesAsync(ct);
        return (new CompletarBiometriaResult(v.Estado, score.Score, score.Motivo), null);
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
            string.Equals(v.Parte, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is { Estado: BiometricEstados.Aprobado })
            return (IniciarBiometriaHandler.ToDto(existing, now), null);

        // Actor de la parte (ActorType guarda el rol: "comprador"/"vendedor").
        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, normalized, StringComparison.OrdinalIgnoreCase));
        if (actor is null)
            return (null, "actor_requerido");

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
            validation.Estado = BiometricEstados.Aprobado;
            validation.Score = MockScore;
            validation.Detalle = detalle;
            validation.Nombre = actor.FullName;
            validation.TipoDoc = actor.DocumentType;
            validation.Documento = actor.DocumentNumber;
            validation.Email = actor.Email ?? string.Empty;
            validation.ValidadoAt = now;
            validation.UpdatedAt = now;
        }
        else
        {
            validation = new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                Parte = normalized,
                Nombre = actor.FullName,
                TipoDoc = actor.DocumentType,
                Documento = actor.DocumentNumber,
                Email = actor.Email ?? string.Empty,
                Estado = BiometricEstados.Aprobado,
                TokenHash = BiometricToken.Hash(BiometricToken.Generate()),
                ExpiresAt = now.AddHours(BiometricRules.TokenTtlHoras),
                Intentos = 0,
                MaxIntentos = BiometricRules.MaxIntentos,
                Score = MockScore,
                Detalle = detalle,
                ValidadoAt = now,
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

using System.Text.Json;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Resultado terminal de una validación de identidad, agnóstico de la fuente (webhook o consulta/poll).
/// <paramref name="ProviderStatus"/> y <paramref name="SanitizedPayload"/> ya vienen sanitizados (sin PII/OCR).
/// </summary>
public sealed record IdentityValidationTerminalResult(
    bool Approved, string? ProviderStatus, string SanitizedPayload, int? Score, string? CertificateHash = null);

/// <summary>
/// Punto ÚNICO donde un resultado terminal (aprobado|rechazado) se aplica a una validación de identidad:
/// setea el estado (vía <see cref="ProcedureInstanceBiometricValidation.Approve"/> o rechazo), estampa
/// provider_status/payload/score y emite el evento <see cref="IdentityValidationCompleted"/> (outbox).
/// Idempotente: si la validación ya está en estado terminal no hace nada. NO persiste — el caller decide
/// cuándo hacer <c>SaveChanges</c>. Lo usan por igual el webhook y la reconciliación por consulta, para que
/// ambos caminos apliquen EXACTAMENTE la misma lógica (aprobación + vigencia + evento).
///
/// RF40 — política de improntas por IA (informar, no bloquear): cuando el score de coincidencia queda por
/// debajo del umbral (<see cref="ImprontaValidationPolicyOptions.MatchThreshold"/>), encola un evento de
/// bitácora <c>validacion_biometrica_advertencia</c> (append-only, visible en el timeline) SIN alterar el
/// veredicto ni bloquear la radicación. Los parámetros <paramref name="repo"/>/<paramref name="policy"/> son
/// opcionales: sin ellos (p. ej. en tests que no ejercitan la política) el applier mantiene su comportamiento
/// previo intacto.
///
/// <para><b>HU #14179</b> — al aprobar o rechazar la identidad de una parte del trámite, encola UNA fila
/// <c>dato_modificado</c> con la frase corta («Identidad del comprador: aprobada» / «rechazada»; la parte
/// en minúscula). Anterior vacío. No guarda imagen, puntaje ni el cuerpo del proveedor. La misma frase
/// (Ordinal tras trim) no genera otra fila. No escribe <see cref="IdentityValidationAuditEvent"/>.</para>
/// </summary>
public sealed class IdentityValidationResultApplier(
    IIdentityValidationEventPublisher events,
    IProcedureInstanceRepository? repo = null,
    ImprontaValidationPolicyOptions? policy = null)
{
    private const string EtiquetaIdentidad = "Validación de identidad";
    private const string CampoIdentidad = "identidad";

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    /// <summary>Frases ya encoladas en este scope (aún no visibles en la consulta de bitácora).</summary>
    private readonly HashSet<(Guid InstanceId, string Frase)> _frasesEmitidas = new();

    public async Task<bool> ApplyAsync(
        ProcedureInstanceBiometricValidation v,
        IdentityValidationTerminalResult result,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(result);

        // Bug #13055 — trámite anulado o revocado: la validación queda como estaba (tampoco se enriquece
        // el hash de HU #11015) y no se emite evento. Cubre webhook, consulta, worker y botón manual.
        if (v.CongeladaPorTramite)
            return false;

        // Idempotencia: los estados terminales no se re-aplican ni re-emiten evento.
        // HU #11015 — EXCEPCIÓN: una validación ya APROBADA a la que le falta la serie del certificado
        // sí se enriquece cuando un resultado posterior la trae. Pasa cuando el webhook se pierde y
        // aprueba la reconciliación por GET, que no siempre expone `firmaSerie`: sin este enriquecimiento
        // el hash quedaba null PARA SIEMPRE y el sello de firma del FUR salía sin valor. No cambia el
        // estado ni re-emite eventos: solo rellena el hueco (mismo criterio que AdminIdentityValidation).
        // EXCEPCIÓN 2: el aprobado de Kyverum reemplaza un rechazo aplicado antes de agotar los intentos (la persona
        // aprobó en un intento posterior del mismo enlace). Se aplica como cualquier aprobación y emite su evento.
        var aprobadoTrasRechazoPrematuro = result.Approved && BiometricRules.EsRechazoKyverumConIntentosDisponibles(v);
        if ((v.Status is BiometricEstados.Aprobado or BiometricEstados.Rechazado) && !aprobadoTrasRechazoPrematuro)
        {
            if (v.Status == BiometricEstados.Aprobado
                && string.IsNullOrWhiteSpace(v.CertificateHash)
                && result.Approved
                && !string.IsNullOrWhiteSpace(result.CertificateHash))
            {
                v.CertificateHash = result.CertificateHash;
                v.UpdatedAt = now;
            }

            return false;
        }

        if (result.Approved)
        {
            v.Approve(now); // estado + validated_at + estampa valid_until + updated_at
            // Estampa la serie/hash del certificado (firmaSerie) al aprobar; no la sobreescribe con null si el
            // origen no la trae. Gana quien alcanza el estado terminal primero: normalmente el webhook (que sí
            // trae firmaSerie); si aprueba antes la reconciliación por GET (webhook perdido) y Kyverum no expone
            // firmaSerie ahí, el hash queda null y el sello del FUR muestra la serie ausente — degradación
            // acotada al caso "el webhook nunca llegó".
            if (!string.IsNullOrWhiteSpace(result.CertificateHash))
                v.CertificateHash = result.CertificateHash;
        }
        else
        {
            v.Status = BiometricEstados.Rechazado;
            v.UpdatedAt = now;
        }

        v.ProviderStatus = result.ProviderStatus;
        v.ProviderPayload = result.SanitizedPayload;
        v.Score = result.Score;

        // RF40 — política informar: por debajo del umbral se deja constancia en el timeline sin bloquear.
        // Solo aplica a validaciones ligadas a un trámite (ProcedureInstanceId != null); las standalone
        // no tienen instancia donde escribir el evento de bitácora — HU #10865.
        if (repo is not null && policy is not null && result.Score is int score && v.ProcedureInstanceId.HasValue)
        {
            var decision = ImprontaPolicyEvaluator.Evaluate(score, policy.MatchThreshold, policy.BlockBelowThreshold);
            if (decision != ImprontaPolicyDecision.Ok)
            {
                await repo.AddEventAsync(new ProcedureInstanceEvent
                {
                    Id = Guid.NewGuid(),
                    TenantId = v.TenantId,
                    ProcedureInstanceId = v.ProcedureInstanceId.Value,
                    Tipo = "validacion_biometrica_advertencia",
                    Payload = JsonSerializer.Serialize(new
                    {
                        score,
                        threshold = policy.MatchThreshold,
                        decision = decision.ToString().ToLowerInvariant(),
                        parte = v.PartyRole,
                    }),
                    CreatedAt = now,
                }, ct);
            }
        }

        await RegistrarFraseEnBitacoraAsync(v, result.Approved, now, ct);

        await events.PublishAsync(new IdentityValidationCompleted
        {
            TenantId = v.TenantId,
            ProcedureInstanceId = v.ProcedureInstanceId,
            ValidationId = v.Id,
            Provider = v.Provider,
            Parte = v.PartyRole,
            Estado = v.Status,
            ProviderStatus = result.ProviderStatus,
            Score = result.Score,
        }, ct);

        return true;
    }

    /// <summary>
    /// Encola la frase corta de la parte sin volver a decidir el resultado.
    /// La simulación del gestor aprueba la fila y no pasa por <see cref="ApplyAsync"/>,
    /// que sale sin escribir si el estado ya es terminal. No usa el catálogo de etiquetas.
    /// Sin repositorio, sin trámite o sin parte, no hay fila. La misma frase no se encola otra vez.
    /// </summary>
    public Task EncolarFraseDeParteAsync(
        ProcedureInstanceBiometricValidation v,
        bool approved,
        DateTimeOffset now,
        CancellationToken ct = default)
        => RegistrarFraseEnBitacoraAsync(v, approved, now, ct);

    private async Task RegistrarFraseEnBitacoraAsync(
        ProcedureInstanceBiometricValidation v,
        bool approved,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (repo is null || v.ProcedureInstanceId is not Guid instanceId)
            return;

        var parte = ParteEnMinuscula(v.PartyRole);
        if (parte is null)
            return;

        var frase = approved
            ? $"Identidad del {parte}: aprobada"
            : $"Identidad del {parte}: rechazada";

        var clave = (instanceId, frase);
        if (_frasesEmitidas.Contains(clave))
            return;

        if (await FraseYaRegistradaAsync(repo, instanceId, v.TenantId, frase, ct))
        {
            _frasesEmitidas.Add(clave);
            return;
        }

        _frasesEmitidas.Add(clave);
        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = v.TenantId,
            ProcedureInstanceId = instanceId,
            Tipo = ProcedureFieldChangeRecorder.EventType,
            Payload = JsonSerializer.Serialize(new
            {
                etiqueta = EtiquetaIdentidad,
                campo = CampoIdentidad,
                anterior = "",
                nuevo = frase,
            }, PayloadJson),
            CreatedAt = now.ToUniversalTime(),
            CreatedBy = null,
        }, ct);
    }

    private static async Task<bool> FraseYaRegistradaAsync(
        IProcedureInstanceRepository repositorio,
        Guid instanceId,
        Guid tenantId,
        string frase,
        CancellationToken ct)
    {
        const int pageSize = 100;
        var skip = 0;
        for (var pageIndex = 0; pageIndex < 1000; pageIndex++)
        {
            var page = await repositorio.GetModificacionesPageAsync(instanceId, tenantId, skip, pageSize, ct);
            if (page is not { } found)
                return false;

            foreach (var item in found.Items)
            {
                if (!string.Equals(item.Campo, CampoIdentidad, StringComparison.Ordinal))
                    continue;

                var existente = string.IsNullOrWhiteSpace(item.Nuevo) ? string.Empty : item.Nuevo.Trim();
                if (string.Equals(existente, frase, StringComparison.Ordinal))
                    return true;
            }

            skip += pageSize;
            if (skip >= found.Total)
                return false;
        }

        return false;
    }

    private static string? ParteEnMinuscula(string? partyRole)
    {
        if (string.IsNullOrWhiteSpace(partyRole))
            return null;
        var parte = partyRole.Trim().ToLowerInvariant();
        return parte.Length == 0 ? null : parte;
    }
}

using System.Text.Json;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.Identity;

/// <summary>Resultado del consumo de un <see cref="IdentityValidationCompleted"/> (telemetría/idempotencia).</summary>
public sealed record IdentityValidationConsumeResult(
    Guid ValidationId,
    string Estado,
    int Matched,
    int Processed,
    IReadOnlyList<string> Skipped);

/// <summary>
/// Consumidor del evento <see cref="IdentityValidationCompleted"/> (HU #10349, AC4/AC5 — fase 2). Cuando
/// la validación de identidad de un sujeto resulta <c>aprobado</c>, encadena automáticamente la firma de
/// TODOS los trámites PENDIENTES del tenant donde ese sujeto (tipoDoc + documento) firma:
/// <list type="bullet">
///   <item><b>Con compraventa autogenerada</b> → <see cref="SolicitarFirmaHandler"/> (compraventa) de
///   cada parte del sujeto; idempotente.</item>
///   <item><b>Sin compraventa</b> → <see cref="GenerarFurHandler"/> (FUR) una vez: el generador resuelve la
///   identidad vigente de la persona y estampa el sello.</item>
/// </list>
/// <para><b>Bug #13194 (punto 4).</b> Antes solo se firmaban los borradores finalizados donde el sujeto era
/// el ACTOR de la misma parte validada. Eso dejaba fuera (1) a la persona jurídica, cuyo sujeto es el
/// representante legal en <c>metadata</c> y no el NIT del actor, (2) los trámites donde la persona es la
/// otra parte, y (3) los radicados que aún no llegan al organismo para decidir. El conjunto de estados vive
/// en <see cref="TramiteFirmaPendiente"/>; la búsqueda por sujeto, en
/// <see cref="IProcedureInstanceRepository.ListPendientesDeFirmaPorSujetoAsync"/>. Siempre dentro del tenant
/// de la validación: la identidad no se reutiliza entre tenants.</para>
/// <para>Procesa en orden determinista (<c>draft_finalized_at</c>, consecutivo) y registra un evento
/// <c>firma_auto_solicitada</c> correlacionado con el <c>validationId</c>. Ese evento es también la llave de
/// idempotencia: una re-entrega del mismo evento omite los trámites que ya lo tienen. Tolera errores por
/// trámite (uno aún no apto no detiene al resto). La firma NUNCA se dispara desde el webhook.</para>
/// <para><b>Review PR #510 (MAYOR-2, versión acotada).</b> (i) Cada trámite corre en su propio SAVEPOINT
/// (<see cref="ISavepointScope"/>) con try/catch: una excepción de un trámite se revierte solo hasta su
/// savepoint, se registra sin PII y el lote sigue; la transacción del outbox no se aborta. (ii) Se omiten los
/// trámites que ya están firmados por esta persona (<see cref="OmitidoYaFirmado"/>). (iii) Si el sujeto no es
/// actor de ninguna parte del trámite se omite (<see cref="OmitidoSujetoNoEsParte"/>): ya NO se cae a la
/// parte validada (L2 de security). Deuda documentada: una cola por trámite en lugar del lote dentro de la
/// transacción del outbox.</para>
/// </summary>
public sealed class IdentityValidationCompletedConsumer(
    IProcedureInstanceRepository repo,
    SolicitarFirmaHandler firmaHandler,
    GenerarFurHandler furHandler,
    ISavepointScope? savepoints = null,
    ILogger<IdentityValidationCompletedConsumer>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<IdentityValidationCompletedConsumer>.Instance;

    /// <summary>Motivo de omisión: el trámite ya tiene la firma de esta persona (FUR vigente o compraventa firmada).</summary>
    public const string OmitidoYaFirmado = "ya_firmado";

    /// <summary>Motivo de omisión: la persona validada no es el sujeto de identidad de ninguna parte.</summary>
    public const string OmitidoSujetoNoEsParte = "sujeto_no_es_parte";

    /// <summary>Motivo de omisión: el trámite lanzó una excepción; se revirtió su savepoint y el lote siguió.</summary>
    public const string OmitidoExcepcion = "excepcion";

    /// <summary>Tipo del evento de bitácora del lote (y llave de idempotencia por validación).</summary>
    public const string EventoFirmaAutoSolicitada = "firma_auto_solicitada";

    /// <summary>Motivo de omisión cuando el trámite ya se firmó con esta misma validación.</summary>
    public const string OmitidoYaAplicado = "ya_aplicado";

    public async Task<IdentityValidationConsumeResult> HandleAsync(
        IdentityValidationCompleted evt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        // Solo 'aprobado' encadena el auto-flujo (AC4). Otros estados: no-op.
        if (!string.Equals(evt.Estado, BiometricEstados.Aprobado, StringComparison.OrdinalIgnoreCase))
            return new IdentityValidationConsumeResult(evt.ValidationId, evt.Estado, 0, 0, []);

        // El sujeto (tipoDoc + documento) no viaja en el evento sanitizado: se resuelve desde la validación.
        var validation = await repo.GetBiometricByIdAsync(evt.ValidationId, ct);
        if (validation is null
            || string.IsNullOrWhiteSpace(validation.DocumentType)
            || string.IsNullOrWhiteSpace(validation.DocumentNumber))
            return new IdentityValidationConsumeResult(evt.ValidationId, evt.Estado, 0, 0, []);

        var tipoDoc = validation.DocumentType.Trim();
        var documento = validation.DocumentNumber.Trim();
        var aprobadaEn = validation.ValidatedAt ?? validation.CreatedAt;

        var instances = await repo.ListPendientesDeFirmaPorSujetoAsync(validation.TenantId, tipoDoc, documento, ct);
        if (instances.Count == 0)
            return new IdentityValidationConsumeResult(evt.ValidationId, evt.Estado, 0, 0, []);

        var yaAplicados = await repo.ListInstanceIdsConEventoDeValidacionAsync(
            validation.TenantId, EventoFirmaAutoSolicitada, evt.ValidationId, ct);

        var processed = 0;
        var skipped = new List<string>();

        foreach (var instance in instances)
        {
            // Idempotencia frente a re-entregas del outbox: este trámite ya se firmó con esta validación.
            if (yaAplicados.Contains(instance.Id))
            {
                skipped.Add($"{instance.ReferenceNumber}:{OmitidoYaAplicado}");
                continue;
            }

            // Defensa en profundidad: el repositorio ya filtra, pero el estado manda al APLICAR.
            if (!TramiteFirmaPendiente.EntraEnLoteDeFirma(
                    instance.Status, instance.SubsanacionActiva, instance.DraftFinalizedAt is not null))
            {
                skipped.Add($"{instance.ReferenceNumber}:estado_{instance.Status}");
                continue;
            }

            // (iii) L2 — sin parte cuyo sujeto sea esta persona no se firma nada (antes caía a la parte validada).
            var partes = PartesDelSujeto(instance, tipoDoc, documento);
            if (partes.Count == 0)
            {
                skipped.Add($"{instance.ReferenceNumber}:{OmitidoSujetoNoEsParte}");
                continue;
            }

            // ADR-0051 — lo que decide el encadenamiento es si el expediente autogenera compraventa
            // (ADR-0035), no la familia. En `TRASPASO_UNILATERAL` (familia TRASPASO sin compraventa) pedir
            // la firma de un documento que `FurCommand` no genera dejaba el trámite SIN FUR.
            var profile = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
            var generaCompraventa = profile.GeneratesSaleDocumentAllowed(instance.ProcedureType?.Family);

            // (ii) Ya firmado por esta persona: nada que hacer.
            if (YaFirmado(instance, partes, generaCompraventa, aprobadaEn))
            {
                skipped.Add($"{instance.ReferenceNumber}:{OmitidoYaFirmado}");
                continue;
            }

            string? error;
            try
            {
                // (i) Savepoint por trámite: si este falla, solo se revierte lo suyo y el lote sigue.
                error = savepoints is null
                    ? await FirmarAsync(instance, validation.TenantId, evt.ValidationId, partes, generaCompraventa, ct)
                    : await savepoints.EjecutarAsync(
                        () => FirmarAsync(instance, validation.TenantId, evt.ValidationId, partes, generaCompraventa, ct), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                IdentityValidationConsumerLog.TramiteFallido(_logger, ex.GetType().Name, instance.Id, evt.ValidationId);
                skipped.Add($"{instance.ReferenceNumber}:{OmitidoExcepcion}");
                continue;
            }

            if (error is not null)
            {
                // Trámite aún no apto (p.ej. organismo/gate): se omite sin romper el lote.
                skipped.Add($"{instance.ReferenceNumber}:{error}");
                continue;
            }

            processed++;
        }

        return new IdentityValidationConsumeResult(
            evt.ValidationId, evt.Estado, instances.Count, processed, skipped);
    }

    /// <summary>
    /// Firma un trámite (compraventa por parte, o un FUR) y deja la bitácora/llave de idempotencia. Devuelve
    /// el código de error del handler si el trámite aún no es apto, o null si quedó firmado.
    /// </summary>
    private async Task<string?> FirmarAsync(
        ProcedureInstance instance, Guid tenantId, Guid validationId, List<string> partes,
        bool generaCompraventa, CancellationToken ct)
    {
        string? error = null;
        string accion;
        if (generaCompraventa)
        {
            accion = "firma_compraventa";
            foreach (var parte in partes)
            {
                (_, error) = await firmaHandler.HandleAsync(
                    instance.Id, tenantId, new SolicitarFirmaInput(parte, null), ct);
                if (error is not null)
                    break;
            }
        }
        else
        {
            // Un solo FUR basta aunque la persona firme por dos partes: el generador sella todas.
            accion = "fur";
            (_, error) = await furHandler.HandleAsync(instance.Id, tenantId, ct);
        }

        if (error is not null)
            return error;

        // Bitácora correlacionada con la validación (AC5) y llave de idempotencia.
        repo.Add(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = EventoFirmaAutoSolicitada,
            Payload = JsonSerializer.Serialize(new
            {
                validation_id = validationId,
                parte = partes[0],
                partes,
                estado = instance.Status,
                modalidad = instance.FamilyCode,
                accion,
            }),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await repo.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// (ii) ¿El trámite ya tiene la firma de esta persona? Con compraventa: cada parte del sujeto ya firmó el
    /// contrato. Sin compraventa: hay un FUR generado por el sistema DESPUÉS de la aprobación (lleva el sello
    /// de esta validación) y no quedó desactualizado por un cambio posterior del expediente.
    /// </summary>
    private static bool YaFirmado(
        ProcedureInstance instance, List<string> partes, bool generaCompraventa, DateTimeOffset aprobadaEn)
    {
        if (generaCompraventa)
        {
            return partes.All(parte => instance.Signatures.Any(s =>
                string.Equals(s.Parte, parte, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.DocTipo, SignatureDocTipos.Compraventa, StringComparison.OrdinalIgnoreCase)
                && s.Estado == SignatureEstados.Firmada));
        }

        var fur = instance.Attachments
            .Where(a => string.Equals(a.Tipo, FurVigenciaExpediente.TipoFur, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefault();
        return fur is not null
            && !string.Equals(fur.Source, "user", StringComparison.OrdinalIgnoreCase)
            && fur.UploadedAt >= aprobadaEn
            && !FurVigenciaExpediente.FurDesactualizado(instance);
    }

    /// <summary>
    /// Partes (comprador/vendedor) del trámite cuyo SUJETO DE IDENTIDAD es la persona validada: el actor
    /// en persona natural, el representante legal en persona jurídica (<see cref="IdentitySubjectResolver"/>).
    /// Review PR #510 (L2): vacío si ninguna parte es la persona — el llamador omite el trámite.
    /// </summary>
    private static List<string> PartesDelSujeto(
        ProcedureInstance instance, string tipoDoc, string documento)
    {
        var partes = instance.Actors
            .Where(a => a.ActorType is BiometricRules.ParteComprador or BiometricRules.ParteVendedor)
            .Where(a =>
            {
                var sujeto = IdentitySubjectResolver.For(a);
                return string.Equals(sujeto.TipoDocumento?.Trim(), tipoDoc, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(sujeto.NumeroDocumento?.Trim(), documento, StringComparison.OrdinalIgnoreCase);
            })
            .Select(a => a.ActorType)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return partes;
    }
}

/// <summary>Logs sin PII (solo ids y tipo de excepción) del consumidor de identidad aprobada.</summary>
internal static partial class IdentityValidationConsumerLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Bug #13194 — la firma automática del trámite {InstanceId} (validación {ValidationId}) falló con {ExceptionType}; se revirtió su savepoint y el lote siguió.")]
    public static partial void TramiteFallido(ILogger logger, string exceptionType, Guid instanceId, Guid validationId);
}

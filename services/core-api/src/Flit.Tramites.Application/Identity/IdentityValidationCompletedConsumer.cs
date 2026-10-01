using System.Text.Json;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Estados;

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
/// </summary>
public sealed class IdentityValidationCompletedConsumer(
    IProcedureInstanceRepository repo,
    SolicitarFirmaHandler firmaHandler,
    GenerarFurHandler furHandler)
{
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
        var parteValidada = validation.PartyRole ?? BiometricRules.ParteComprador;

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

            var partes = PartesDelSujeto(instance, tipoDoc, documento, parteValidada);

            // ADR-0051 — lo que decide el encadenamiento es si el expediente autogenera compraventa
            // (ADR-0035), no la familia. En `TRASPASO_UNILATERAL` (familia TRASPASO sin compraventa) pedir
            // la firma de un documento que `FurCommand` no genera dejaba el trámite SIN FUR.
            var profile = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
            var generaCompraventa = profile.GeneratesSaleDocumentAllowed(instance.ProcedureType?.Family);

            string? error = null;
            string accion;
            if (generaCompraventa)
            {
                accion = "firma_compraventa";
                foreach (var parte in partes)
                {
                    (_, error) = await firmaHandler.HandleAsync(
                        instance.Id, validation.TenantId, new SolicitarFirmaInput(parte, null), ct);
                    if (error is not null)
                        break;
                }
            }
            else
            {
                // Un solo FUR basta aunque la persona firme por dos partes: el generador sella todas.
                accion = "fur";
                (_, error) = await furHandler.HandleAsync(instance.Id, validation.TenantId, ct);
            }

            if (error is not null)
            {
                // Trámite aún no apto (p.ej. organismo/gate): se omite sin romper el lote.
                skipped.Add($"{instance.ReferenceNumber}:{error}");
                continue;
            }

            // Bitácora correlacionada con la validación (AC5) y llave de idempotencia.
            repo.Add(new ProcedureInstanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = validation.TenantId,
                ProcedureInstanceId = instance.Id,
                Tipo = EventoFirmaAutoSolicitada,
                Payload = JsonSerializer.Serialize(new
                {
                    validation_id = evt.ValidationId,
                    parte = partes[0],
                    partes,
                    estado = instance.Status,
                    modalidad = instance.FamilyCode,
                    accion,
                }),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await repo.SaveChangesAsync(ct);
            processed++;
        }

        return new IdentityValidationConsumeResult(
            evt.ValidationId, evt.Estado, instances.Count, processed, skipped);
    }

    /// <summary>
    /// Partes (comprador/vendedor) del trámite cuyo SUJETO DE IDENTIDAD es la persona validada: el actor
    /// en persona natural, el representante legal en persona jurídica (<see cref="IdentitySubjectResolver"/>).
    /// Si el grafo no trae actores (no debería: el repositorio los incluye), cae a la parte validada.
    /// </summary>
    private static List<string> PartesDelSujeto(
        ProcedureInstance instance, string tipoDoc, string documento, string parteValidada)
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
        return partes.Count > 0 ? partes : [parteValidada];
    }
}

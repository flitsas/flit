using System.Text.Json;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;

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
/// la validación de identidad de un sujeto resulta <c>aprobado</c>, encadena automáticamente el siguiente
/// paso de TODOS los borradores finalizados (<c>draft</c> + <c>draft_finalized_at NOT NULL</c>) del tenant
/// donde ese sujeto (tipoDoc + documento) es actor de la parte validada:
/// <list type="bullet">
///   <item><b>Traspaso</b> → <see cref="SolicitarFirmaHandler"/> (compraventa) de la parte; idempotente.</item>
///   <item><b>Matrícula</b> → <see cref="GenerarFurHandler"/> (FUR); sin firma de compraventa.</item>
/// </list>
/// Procesa en orden determinista (lo garantiza el repositorio: <c>draft_finalized_at</c> ASC, luego
/// <c>reference_number</c>) y registra un evento de bitácora <c>firma_auto_solicitada</c> correlacionado
/// con el <c>validationId</c>. Tolera errores por trámite (un borrador aún incompleto no detiene al resto).
/// La idempotencia frente a re-entregas la dan los handlers (firma/FUR reusan/reemplazan) y el sellado de
/// <c>published_at</c> en la outbox por el procesador. La firma NUNCA se dispara desde el webhook.
/// </summary>
public sealed class IdentityValidationCompletedConsumer(
    IProcedureInstanceRepository repo,
    SolicitarFirmaHandler firmaHandler,
    GenerarFurHandler furHandler)
{
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
        if (validation is null)
            return new IdentityValidationConsumeResult(evt.ValidationId, evt.Estado, 0, 0, []);

        var parte = validation.Parte ?? BiometricRules.ParteComprador;
        var instances = await repo.ListDraftFinalizedByActorAsync(
            validation.TenantId, parte, validation.TipoDoc, validation.Documento, ct);

        var processed = 0;
        var skipped = new List<string>();

        foreach (var instance in instances)
        {
            var esTraspaso = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada)
                             == TramiteModalidadEntrada.Traspaso;

            string? error;
            string accion;
            if (esTraspaso)
            {
                accion = "firma_compraventa";
                (_, error) = await firmaHandler.HandleAsync(
                    instance.Id, validation.TenantId, new SolicitarFirmaInput(parte, null), ct);
            }
            else
            {
                accion = "fur";
                (_, error) = await furHandler.HandleAsync(instance.Id, validation.TenantId, ct);
            }

            if (error is not null)
            {
                // Borrador aún no apto (p.ej. organismo/gate): se omite sin romper el lote.
                skipped.Add($"{instance.ReferenceNumber}:{error}");
                continue;
            }

            // Bitácora correlacionada con la validación (AC5).
            repo.Add(new ProcedureInstanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = validation.TenantId,
                ProcedureInstanceId = instance.Id,
                Tipo = "firma_auto_solicitada",
                Payload = JsonSerializer.Serialize(new
                {
                    validation_id = evt.ValidationId,
                    parte,
                    modalidad = instance.ModalidadEntrada,
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
}

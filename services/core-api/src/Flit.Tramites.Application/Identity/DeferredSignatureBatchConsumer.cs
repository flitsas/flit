using System.Text.Json;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Identity;

/// <summary>Resultado de aplicar el lote de firma a posteriori (telemetría y traza).</summary>
public sealed record DeferredSignatureBatchResult(
    Guid ValidationId,
    int Marcados,
    int Firmados,
    int Descartados,
    IReadOnlyList<string> Omitidos,
    int DirectorioActualizado);

/// <summary>
/// HU #11196 (AC2–AC6) — cuando la validación de identidad de una persona se aprueba, firma de una todos
/// los trámites del tenant que quedaron marcados esperándola. Antes había que volver trámite por trámite.
///
/// <para>Es un consumidor del mismo evento <see cref="IdentityValidationCompleted"/> que ya usa
/// <see cref="IdentityValidationCompletedConsumer"/>, pero separado a propósito: son dos políticas
/// distintas (aquella encadena borradores finalizados por actor; esta aplica marcas explícitas) y
/// mezclarlas haría imposible razonar sobre cuál disparó qué.</para>
///
/// <para><b>El estado se revalida AL APLICAR, no al marcar</b> (D1/D2): entre la marca y la aprobación
/// pueden pasar días y el trámite pudo radicarse. Los que ya no son elegibles se cierran como
/// <c>descartada</c> con su motivo, no se dejan pendientes para siempre ni se firman a la fuerza.</para>
///
/// <para>Cada trámite deja su propio evento de bitácora <c>firma_diferida_aplicada</c> (AC6): es una
/// operación masiva y diferida, así que un best-effort silencioso dejaría al gestor sin forma de saber
/// qué pasó — el precedente del <c>DbSignatureVaultReader</c> del Feature #11004.</para>
/// </summary>
public sealed class DeferredSignatureBatchConsumer(
    IProcedureInstanceRepository repo,
    IDeferredSignatureMarkRepository marks,
    ITramiteFirmaAplicador firmaAplicador,
    IRepresentanteLegalIdentityUpdater? directoryUpdater = null)
{
    private readonly IRepresentanteLegalIdentityUpdater _directoryUpdater =
        directoryUpdater ?? NullRepresentanteLegalIdentityUpdater.Instance;

    public async Task<DeferredSignatureBatchResult> HandleAsync(
        IdentityValidationCompleted evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (!string.Equals(evt.Estado, BiometricEstados.Aprobado, StringComparison.OrdinalIgnoreCase))
            return Vacio(evt.ValidationId);

        // El documento del sujeto no viaja en el evento sanitizado: se resuelve desde la validación.
        var validation = await repo.GetBiometricByIdAsync(evt.ValidationId, ct);
        if (validation is null
            || string.IsNullOrWhiteSpace(validation.DocumentType)
            || string.IsNullOrWhiteSpace(validation.DocumentNumber))
            return Vacio(evt.ValidationId);

        var pendientes = await marks.ListPendientesByRepresentativeAsync(
            validation.TenantId, validation.DocumentType, validation.DocumentNumber, ct);

        if (pendientes.Count == 0)
            return Vacio(evt.ValidationId);

        var now = DateTimeOffset.UtcNow;
        var firmados = 0;
        var descartados = 0;
        var omitidos = new List<string>();

        foreach (var mark in pendientes)
        {
            var instance = await repo.GetByIdAsync(mark.ProcedureInstanceId, mark.TenantId, ct);
            if (instance is null)
            {
                mark.Descartar("tramite_inexistente", now);
                descartados++;
                continue;
            }

            // AC3 — revalidación del estado en el momento de aplicar.
            if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            {
                mark.Descartar($"estado_{instance.Status}", now);
                descartados++;
                await RegistrarEventoAsync(mark, instance, evt.ValidationId, "descartada", now, ct);
                continue;
            }

            var (accion, error) = await firmaAplicador.AplicarAsync(instance, mark.TenantId, mark.PartyRole, ct);
            if (error is not null)
            {
                // El trámite sigue marcado: no es elegible AHORA (p.ej. le falta un gate), pero puede
                // serlo cuando se corrija. Descartarlo aquí perdería la marca sin que nadie lo pida.
                omitidos.Add($"{instance.ReferenceNumber}:{error}");
                continue;
            }

            mark.Aplicar(evt.ValidationId, now);
            firmados++;
            await RegistrarEventoAsync(mark, instance, evt.ValidationId, accion, now, ct);
        }

        await marks.SaveChangesAsync(ct);

        // AC4 — la firma activa del representante en la compañía queda actualizada. Si esa persona no
        // está registrada en el directorio (el caso de la HU #11195), no hay nada que actualizar.
        var actualizados = firmados == 0
            ? 0
            : await _directoryUpdater.ActualizarIdentidadVigenteAsync(
                validation.TenantId, validation.DocumentType, validation.DocumentNumber, evt.ValidationId, ct);

        return new DeferredSignatureBatchResult(
            evt.ValidationId, pendientes.Count, firmados, descartados, omitidos, actualizados);
    }

    /// <summary>AC6 — traza por trámite: se firmó (o se descartó) por la aplicación diferida y con qué validación.</summary>
    private async Task RegistrarEventoAsync(
        DeferredSignatureMark mark,
        ProcedureInstance instance,
        Guid validationId,
        string accion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = mark.TenantId,
            ProcedureInstanceId = mark.ProcedureInstanceId,
            Tipo = "firma_diferida_aplicada",
            Payload = JsonSerializer.Serialize(new
            {
                validation_id = validationId,
                parte = mark.PartyRole,
                modalidad = instance.ModalidadEntrada,
                accion,
                estado_marca = mark.Estado,
                motivo = mark.DiscardedReason,
            }),
            CreatedAt = now,
        }, ct);
    }

    private static DeferredSignatureBatchResult Vacio(Guid validationId) =>
        new(validationId, 0, 0, 0, [], 0);
}

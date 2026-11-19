using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// HU #14051 (Épica #14021) — puerto para avisar que se abrió una subsanación (<c>subsanacion_activa</c> sobre un trámite
/// <c>rechazado</c>). Abrirla NO es una transición de estado y por eso no genera <c>tramites.procedure.state_changed</c>;
/// quien necesite saberlo (hoy, el espejo del portal de persona natural) se engancha aquí. Se invoca ANTES del guardado del
/// caso de uso, para que lo que encole viaje en la misma transacción (outbox). Es opcional: sin implementación registrada
/// el caso de uso se comporta exactamente como antes.
/// </summary>
public interface ITramiteSubsanacionPublisher
{
    Task EnqueueAsync(ProcedureInstance instance, CancellationToken ct = default);
}

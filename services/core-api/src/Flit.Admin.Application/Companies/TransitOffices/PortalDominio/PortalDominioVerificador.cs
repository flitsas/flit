using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;

namespace Flit.Admin.Application.Companies.TransitOffices.PortalDominio;

/// <summary>
/// Comprobación DNS del dominio de un portal, a demanda o por el trabajo programado: el mismo camino
/// (<see cref="PortalDominioEstados.DecidirComprobacion"/> y <see cref="IPortalDominioRepository.AplicarComprobacionAsync"/>);
/// la única diferencia es el enfriamiento de la manual y quién queda como autor.
/// </summary>
public sealed class PortalDominioVerificador(
    IPortalDominioRepository repo, IDnsTxtResolver dns, DomainVerificationOptions opciones, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    /// <summary>Comprobación a demanda. <c>Dominio</c> nulo si el organismo no tiene dominio; <c>Esperar</c> &gt; 0 si es muy pronto.</summary>
    public async Task<(DominioDePortal? Dominio, int Esperar)> VerificarAhoraAsync(Guid transitOfficeId, Guid? actor, CancellationToken ct = default)
    {
        if (await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false) is not { } actual)
            return (null, 0);

        var ahora = _reloj.GetUtcNow();
        if (actual.LastCheckedAt is { } ultima)
        {
            var enfriamiento = TimeSpan.FromSeconds(opciones.ManualCooldownSeconds);
            if (ahora - ultima < enfriamiento)
                return (actual, (int)Math.Ceiling((enfriamiento - (ahora - ultima)).TotalSeconds));
        }

        return (await ComprobarAsync(actual, ahora, actor, null, ct).ConfigureAwait(false), 0);
    }

    /// <summary>Comprobación de un dominio reclamado por el trabajo programado (sin enfriamiento).</summary>
    public async Task ProcesarReclamoAsync(PortalDominioReclamo reclamo, string trabajo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reclamo);
        if (await repo.ObtenerAsync(reclamo.TransitOfficeId, ct).ConfigureAwait(false) is not { } actual)
            return; // retirado entre el reclamo y el proceso

        await ComprobarAsync(actual, _reloj.GetUtcNow(), null, trabajo, ct).ConfigureAwait(false);
    }

    private async Task<DominioDePortal?> ComprobarAsync(DominioDePortal actual, DateTimeOffset ahora, Guid? actor, string? trabajo, CancellationToken ct)
    {
        var resultado = await DomainTxtCheck.EvaluarAsync(dns, actual.Host, actual.VerificationToken, ct).ConfigureAwait(false);
        var decision = PortalDominioEstados.DecidirComprobacion(
            new DomainCheckContext(actual.Status, actual.GraceUntil, actual.CheckAttempts, resultado, ahora), opciones);

        return await repo.AplicarComprobacionAsync(
            actual.TransitOfficeId, decision.NewStatus, decision.StatusReason, decision.VerifiedAt, decision.GraceUntil,
            decision.CheckAttempts, decision.NextCheckAt, ahora, actor, trabajo, ct).ConfigureAwait(false);
    }
}

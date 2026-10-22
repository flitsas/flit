using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.IdentityValidation;

public enum ReconcileMandateSignerIdentityOutcome
{
    /// <summary>Se consultó al proveedor; el estado de la ficha ya refleja el resultado.</summary>
    Ok,

    /// <summary>No existe, está eliminado o no es de ese organismo (404).</summary>
    NotFound,

    /// <summary>Persona jurídica, Formato en blanco o forma de firma baúl: no hay validación que consultar (409).</summary>
    NoRequiereValidacion,

    /// <summary>La ficha aún no tiene validación propia: hay que enviarla (409).</summary>
    SinValidacion,

    /// <summary>El proveedor no respondió (503); el worker lo sigue intentando.</summary>
    ProveedorNoDisponible,
}

public sealed record ReconcileMandateSignerIdentityResult(
    ReconcileMandateSignerIdentityOutcome Outcome,
    string? Status = null,
    bool Updated = false);

/// <summary>
/// «Consultar estado» de la validación propia del mandatario, desde la compañía y desde el hub OT. Hace por la ficha lo
/// mismo que la pantalla de espera del trámite: pregunta a Kyverum y aplica el resultado si el webhook no llegó (p. ej. el
/// usuario aprobó en el segundo intento y la notificación se perdió). Idempotente: con la validación ya resuelta no consulta.
/// </summary>
public sealed class ReconcileMandateSignerIdentityHandler
{
    private readonly IMandateSignerReader _reader;
    private readonly IMandateSignerIdentityReconciler _reconciler;

    public ReconcileMandateSignerIdentityHandler(
        IMandateSignerReader reader,
        IMandateSignerIdentityReconciler reconciler)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
    }

    /// <param name="transitOfficeId">Ruta del hub OT: el mandatario debe pertenecer a ese organismo. Nulo en la ruta de la compañía.</param>
    public async Task<ReconcileMandateSignerIdentityResult> HandleAsync(
        Guid mandateSignerId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var signer = await _reader.GetByIdAsync(mandateSignerId, cancellationToken).ConfigureAwait(false);
        if (signer is null
            || (transitOfficeId is { } office
                && signer.TransitOfficeId != office
                && !signer.TransitOfficeIds.Contains(office)))
        {
            return new ReconcileMandateSignerIdentityResult(ReconcileMandateSignerIdentityOutcome.NotFound);
        }

        if (!MandateSignerIdentityLaunch.RequiresValidation(
                signer.SignerModel, MandateSignerIdentityLaunch.EffectiveMethod(signer)))
        {
            return new ReconcileMandateSignerIdentityResult(ReconcileMandateSignerIdentityOutcome.NoRequiereValidacion);
        }

        var result = await _reconciler.ReconcileAsync(mandateSignerId, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            MandateSignerIdentityReconcileOutcome.Ok => new ReconcileMandateSignerIdentityResult(
                ReconcileMandateSignerIdentityOutcome.Ok, result.Status, result.Updated),
            MandateSignerIdentityReconcileOutcome.SinValidacion => new ReconcileMandateSignerIdentityResult(
                ReconcileMandateSignerIdentityOutcome.SinValidacion),
            _ => new ReconcileMandateSignerIdentityResult(ReconcileMandateSignerIdentityOutcome.ProveedorNoDisponible),
        };
    }
}

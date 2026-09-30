using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Integration;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #13142 (ADR-0066) — punto ÚNICO que arma los insumos de <see cref="MandateSignerDefaultResolver"/> y
/// decide el resultado. Lo usan la pantalla (<c>ListMandateSignerOptionsHandler</c>), el PDF
/// (<c>FurCommand</c>), la aprobación (<c>MandatoApprovalHandler</c> y <c>TramiteLifecycleService</c>) y, en
/// #13144/#13145, el evaluador del gate, así que el firmante es el mismo en todos.
/// </summary>
internal static class MandateSignerPrelacionLoader
{
    /// <summary>
    /// Candidatos del organismo y la compañía (con origen, modelo y forma de firma) + default del OT, con la
    /// firma del baúl vigente completada (el directorio no consulta el baúl) y la prelación resuelta.
    /// </summary>
    /// <param name="tenantId">Tenant de la gestora: contra él se resuelve el baúl del mandatario.</param>
    /// <param name="eleccionOt">Elección explícita del OT (al aprobar).</param>
    /// <param name="guardado">Firmante ya guardado en el trámite (se ignora si no es válido).</param>
    public static async Task<(MandateSignerPrelacion Prelacion, IReadOnlyList<MandateSignerCandidate> Todos)> ResolveAsync(
        IMandateSignerDirectory directory,
        ISignatureVaultPolicy? vaultPolicy,
        Guid transitOfficeId,
        Guid tenantId,
        string? nitMandante,
        MandateOtConfig? config,
        Guid? eleccionOt,
        Guid? guardado,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var candidatos = await directory
            .GetCandidatesAsync(transitOfficeId, tenantId, nitMandante, ct)
            .ConfigureAwait(false);
        candidatos = await EnriquecerBaulAsync(candidatos, vaultPolicy, tenantId, ct).ConfigureAwait(false);

        // Default del OT: entra aunque no esté vinculado a la compañía (AC5). Si ya es candidato se usa ese
        // registro (trae FirmaFisica del vínculo).
        MandateSignerCandidate? defaultDelOt = null;
        if (config?.OtDefaultMandateSignerId is { } otId && otId != Guid.Empty)
        {
            defaultDelOt = candidatos.FirstOrDefault(c => c.Id == otId);
            if (defaultDelOt is null)
            {
                var porId = await directory.GetByIdAsync(otId, ct).ConfigureAwait(false);
                if (porId is not null)
                {
                    defaultDelOt = (await EnriquecerBaulAsync([porId], vaultPolicy, tenantId, ct)
                        .ConfigureAwait(false))[0];
                }
            }
        }

        var prelacion = MandateSignerDefaultResolver.Resolve(
            candidatos, defaultDelOt, eleccionOt, guardado, config?.DefaultMandateSignerId);

        var todos = defaultDelOt is not null && candidatos.All(c => c.Id != defaultDelOt.Id)
            ? [.. candidatos, defaultDelOt]
            : candidatos;
        return (prelacion, todos);
    }

    /// <summary>
    /// Traduce la prelación al resultado que consumen la aprobación y el PDF. Con el nivel ambiguo el
    /// cotejo por la cuenta del usuario que aprueba solo DESEMPATA (ADR-0036 §D9, enmendado por ADR-0066).
    /// </summary>
    public static MandateSignerResolution Decidir(MandateSignerPrelacion prelacion, Guid? approvingUserId)
    {
        ArgumentNullException.ThrowIfNull(prelacion);

        if (prelacion.EleccionInvalida)
        {
            return new MandateSignerResolution(MandateSignerResolutionStatus.RequiereSeleccion, null);
        }

        if (prelacion.Signer is { } signer)
        {
            return new MandateSignerResolution(MandateSignerResolutionStatus.Resolved, signer);
        }

        if (prelacion.Ambiguo)
        {
            return MandateSignerSelector.Resolve(prelacion.Desempate!, approvingUserId, explicitSignerId: null);
        }

        // La marca signs_physically se sigue honrando (P4): quien firma a mano sigue saliendo en el PDF y
        // en la aprobación aunque su firma no cuente como válida para el gate.
        return prelacion.FirmaFisicaPendiente is { } fisico
            ? new MandateSignerResolution(MandateSignerResolutionStatus.Resolved, fisico)
            : new MandateSignerResolution(MandateSignerResolutionStatus.NoConfigurado, null);
    }

    /// <summary>Completa <see cref="MandateSignerCandidate.BaulVigente"/> de los naturales que firman con baúl.</summary>
    public static async Task<IReadOnlyList<MandateSignerCandidate>> EnriquecerBaulAsync(
        IReadOnlyList<MandateSignerCandidate> candidatos,
        ISignatureVaultPolicy? vaultPolicy,
        Guid tenantId,
        CancellationToken ct)
    {
        if (candidatos.Count == 0)
        {
            return candidatos;
        }

        var policy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;
        var resultado = new List<MandateSignerCandidate>(candidatos.Count);
        foreach (var c in candidatos)
        {
            if (c.SignerModel == MandateSignerOrigins.ModeloNatural
                && c.SignatureMethod == MandateSignerOrigins.FormaBaul
                && !string.IsNullOrWhiteSpace(c.Documento))
            {
                // HU #13180/#13180b — el asociado de otra compañía y el default del OT tienen su firma en el baúl de
                // SU compañía: se busca primero en el tenant del trámite y luego en los de sus compañías vinculadas.
                var match = await MandatarioBaulLookup.ResolveAsync(policy, c, tenantId, ct).ConfigureAwait(false);
                resultado.Add(c with { BaulVigente = match is not null });
            }
            else
            {
                resultado.Add(c);
            }
        }

        return resultado;
    }
}

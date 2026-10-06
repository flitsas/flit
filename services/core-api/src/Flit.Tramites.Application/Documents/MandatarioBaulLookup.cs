using Flit.Tramites.Domain.Integration;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// HU #13180b (Feature #13119 F7) — busca la firma del baúl de un MANDATARIO en el tenant correcto. El baúl es de la
/// persona (documento) y vive en el tenant de la compañía que lo registró: para un asociado de otra compañía o para
/// el default del OT NO es el tenant de la compañía del trámite, y buscarla solo allí daba falsos negativos
/// (<c>baul_sin_firma_vigente</c>) que, con la validación en modo <c>block</c>, bloquearían radicaciones válidas.
/// <para>Orden: primero el tenant del trámite (comportamiento histórico) y después los tenants de las compañías
/// vinculadas del mandatario (<see cref="MandateSignerCandidate.VaultTenantIds"/>, mismo criterio de compañías que
/// usa la identidad). Gana la primera firma activa y vigente. Única fuente de la regla: la usan la prelación (gate),
/// el selector y el estampado del PDF, así que los tres coinciden.</para>
/// </summary>
public static class MandatarioBaulLookup
{
    public static async Task<SignatureVaultMatch?> ResolveAsync(
        ISignatureVaultPolicy vaultPolicy,
        MandateSignerCandidate signer,
        Guid tenantDelTramite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vaultPolicy);
        ArgumentNullException.ThrowIfNull(signer);

        if (string.IsNullOrWhiteSpace(signer.Documento))
        {
            return null;
        }

        var tipoDoc = string.IsNullOrWhiteSpace(signer.TipoDocumento) ? "CC" : signer.TipoDocumento.Trim();
        foreach (var tenant in signer.VaultTenants(tenantDelTramite))
        {
            var match = await vaultPolicy
                .ResolveMandatarioAsync(tenant, tipoDoc, signer.Documento.Trim(), cancellationToken)
                .ConfigureAwait(false);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}

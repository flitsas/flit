using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.SignatureVault.ListSignatureVault;

/// <summary>
/// Lista las firmas del baúl de un tenant (activas primero; ADR-0025 §5). Soporta filtrado opcional
/// por documento del representante (AC1, HU #11175) y por vigencia (AC2).
/// </summary>
public sealed class ListSignatureVaultHandler
{
    // Hora de Colombia (UTC-5, sin DST): la vigencia se cuenta por día calendario local, coherente
    // con EstaVigente y el resolutor de firma (ADR-0025 §3).
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private readonly ISignatureVaultReader _reader;
    private readonly TimeProvider _timeProvider;

    public ListSignatureVaultHandler(ISignatureVaultReader reader, TimeProvider timeProvider)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<IReadOnlyList<SignatureVaultResponse>> HandleAsync(
        ListSignatureVaultQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _reader
            .ListByTenantAsync(query.TenantId, cancellationToken).ConfigureAwait(false);

        var result = items.AsEnumerable();

        // AC1 — filtrar por documento del representante si ambos campos vienen informados.
        var docNumber = query.DocumentNumber?.Trim();
        var docType = query.DocumentType?.Trim();
        if (!string.IsNullOrEmpty(docNumber) && !string.IsNullOrEmpty(docType))
        {
            result = result.Where(i =>
                string.Equals(i.DocumentType, docType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.DocumentNumber, docNumber, StringComparison.Ordinal));
        }

        // AC2 — excluir vencidas si soloVigentes = true.
        if (query.SoloVigentes == true)
        {
            var today = DateOnly.FromDateTime(
                _timeProvider.GetUtcNow().ToOffset(ColombiaUtcOffset).DateTime);
            result = result.Where(i => i.Estado == SignatureVaultEstado.Activa
                && today >= i.VigenciaDesde
                && today <= i.VigenciaHasta);
        }

        return [.. result.Select(SignatureVaultResponse.From)];
    }
}

using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Infrastructure.Services;

/// <summary>
/// Adaptador (HU #10522, RF17/RF22) que puentea el resolutor de la matriz documental del gestor
/// (<see cref="IResolvedDocumentMatrixResolver"/>, contexto Admin) al puerto que consume el
/// checklist de trámites (<see cref="IResolvedChecklistMatrixProvider"/>). Mantiene desacoplados
/// los dos contextos: el motor de trámites no referencia el submódulo Admin.
/// <para>
/// Cuando el gestor no tiene documentos configurados para el trámite, el resolutor devuelve una
/// lista vacía y este adaptador la propaga tal cual ⇒ el checklist cae al catálogo plano actual.
/// </para>
/// </summary>
internal sealed class ResolvedChecklistMatrixProvider : IResolvedChecklistMatrixProvider
{
    private readonly IResolvedDocumentMatrixResolver _resolver;

    public ResolvedChecklistMatrixProvider(IResolvedDocumentMatrixResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async Task<IReadOnlyList<ResolvedChecklistDoc>> GetForAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver
            .ResolveAsync(procedureTypeId, transitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (resolved.Count == 0)
        {
            return [];
        }

        return [.. resolved.Select(r =>
            new ResolvedChecklistDoc(
                r.Codigo,
                r.Nombre,
                r.Obligatorio,
                r.OrdenResuelto,
                r.EsDummy,
                r.EsGeneradoSistema))];
    }
}

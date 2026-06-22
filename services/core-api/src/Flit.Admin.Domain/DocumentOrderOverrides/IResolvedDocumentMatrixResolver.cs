namespace Flit.Admin.Domain.DocumentOrderOverrides;

/// <summary>
/// Servicio de dominio que resuelve la matriz documental de un trámite (HU #10196, RF18)
/// aplicando la precedencia <b>Cliente &gt; OT &gt; Default</b> por documento. Los niveles
/// OT y Cliente solo se evalúan cuando se aportan sus referencias.
/// </summary>
public interface IResolvedDocumentMatrixResolver
{
    /// <summary>
    /// Resuelve el orden final de cada documento del trámite. <paramref name="transitOfficeId"/>
    /// y <paramref name="clienteId"/> son opcionales: si no vienen, ese nivel se omite y se
    /// usa el inmediatamente inferior. El resultado se ordena por orden resuelto ascendente.
    /// </summary>
    Task<IReadOnlyList<ResolvedDocumentMatrixItem>> ResolveAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        Guid? clienteId,
        CancellationToken cancellationToken = default);
}

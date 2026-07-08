namespace Flit.Admin.Domain.DocumentOrderOverrides;

/// <summary>
/// Servicio de dominio que resuelve la matriz documental de un trámite (HU #10196, RF18;
/// RF22) aplicando la precedencia <b>OT &gt; Default</b> por documento. El Organismo de
/// Tránsito es el único nivel que reordena; el nivel OT solo se evalúa cuando se aporta su
/// referencia.
/// </summary>
public interface IResolvedDocumentMatrixResolver
{
    /// <summary>
    /// Resuelve el orden final de cada documento del trámite. <paramref name="transitOfficeId"/>
    /// es opcional: si no viene, se usa el orden base del trámite. El resultado se ordena por
    /// orden resuelto ascendente.
    /// </summary>
    Task<IReadOnlyList<ResolvedDocumentMatrixItem>> ResolveAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}

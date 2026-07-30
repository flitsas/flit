using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Storage;

namespace Flit.DataMigration.V1.Orchestration;

/// <summary>
/// Lo que produjo la instancia 1 (data plana).
/// <para>
/// Hay un tipo por instancia y no una jerarquía común a propósito: los tres resultados no
/// comparten un solo campo (<c>LoadResult</c> cuenta campos/actores/historial;
/// <c>AttachmentLoadResult</c>, copiados/redundantes; <c>SnapshotLoadResult</c>,
/// materializados/issues). Un tipo base obligaría a hacer downcast para imprimir, que es la
/// abstracción pagando intereses.
/// </para>
/// </summary>
/// <param name="Provisioned">
/// Copia de <c>TargetEnvironment.Provisioned</c> al terminar. Se toma al final porque la
/// escriben DOS sitios: la resolución del entorno y el bucle por trámite.
/// </param>
public sealed record DataInstanceReport(
    MigrationOrigin Origin,
    IReadOnlyList<LoadResult> Results,
    IReadOnlyList<string> Provisioned)
{
    /// <summary>Equivale al exit code 1 de la consola.</summary>
    public bool HasProblems => Results.Any(r => r.Status == LoadStatus.Quarantined);
}

/// <summary>Lo que produjo la instancia 2 (adjuntos cargados).</summary>
public sealed record AttachmentsInstanceReport(
    MigrationOrigin Origin,
    IReadOnlyList<AttachmentLoadResult> Results,
    CopyMode Mode,
    FileManagerEndpoint Source,
    FileManagerEndpoint Target,
    bool KeepIdentityImages,
    IReadOnlyList<string> UndeclaredColumns)
{
    /// <summary>Equivale al exit code 1 de la consola.</summary>
    public bool HasProblems =>
        Results.Any(r => r.Status == AttachmentLoadStatus.NotMigrated || r.Failed > 0);
}

/// <summary>Lo que produjo la instancia 3 (documentos que V1 genera en caliente).</summary>
public sealed record DocumentsInstanceReport(
    MigrationOrigin Origin,
    IReadOnlyList<SnapshotLoadResult> Results,
    V1SnapshotEndpoint Snapshot,
    FileManagerEndpoint Target)
{
    /// <summary>Equivale al exit code 1 de la consola.</summary>
    public bool HasProblems => Results.Any(r =>
        r.Status is SnapshotLoadStatus.NotMigrated or SnapshotLoadStatus.NotFoundInV1
                 or SnapshotLoadStatus.Failed
        || r.Failed > 0);
}

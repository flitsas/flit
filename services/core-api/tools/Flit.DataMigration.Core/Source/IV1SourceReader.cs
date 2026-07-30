namespace Flit.DataMigration.V1.Source;

/// <summary>
/// Origen de los trámites de V1. Deliberadamente abstracto: el resto del migrador
/// (mapeo y carga) no sabe si los datos vinieron de la base de V1, de un JSON entregado
/// por otro equipo o de un stored procedure. Cambiar la fuente = otra implementación.
/// </summary>
public interface IV1SourceReader
{
    /// <summary>
    /// Lee los trámites indicados. Los ids que no existan simplemente no vuelven:
    /// el llamador compara contra lo pedido para reportarlos como "no encontrados".
    /// </summary>
    Task<IReadOnlyList<V1SourceRecord>> ReadAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken);
}

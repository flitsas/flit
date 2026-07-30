using System.Globalization;
using Flit.DataMigration.V1.Mapping;
using Npgsql;

namespace Flit.DataMigration.V1.Orchestration;

/// <summary>
/// De dónde sale y a dónde va esta migración. Es lo que la consola imprime como encabezado y lo
/// que el API devuelve en el bloque <c>origen</c> de cada respuesta.
/// <para>
/// No es decorativo: 12.807 ids existen en LAS DOS tablas de V1, así que el id por sí solo no
/// identifica un trámite. <see cref="V1Table"/> es la línea que deja ver un tipo equivocado antes
/// de que migre el trámite de otra empresa.
/// </para>
/// <para>
/// <b>Nunca contiene credenciales.</b> Las cadenas de conexión entran por <see cref="Describe"/>,
/// que se queda solo con «base @ host:puerto».
/// </para>
/// </summary>
public sealed record MigrationOrigin(
    string Tipo,
    string KindNombre,
    MigrationInstance Instance,
    string V1Table,
    string ProcedureTypeCode,
    string BatchId,
    string V1Database,
    string V2Database,
    IReadOnlyList<long> Ids,
    bool DryRun)
{
    internal static MigrationOrigin From(MigrationRequest request, string v1Connection, string v2Connection) =>
        new(
            request.Tipo,
            request.Kind.Nombre,
            request.Instance,
            request.Kind.Tables.Master,
            request.Kind.ProcedureTypeCode,
            request.BatchId,
            Describe(v1Connection),
            Describe(v2Connection),
            request.Ids,
            request.DryRun);

    /// <summary>Muestra a qué base apunta, sin filtrar credenciales al log.</summary>
    internal static string Describe(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{builder.Database} @ {builder.Host}:{builder.Port.ToString(CultureInfo.InvariantCulture)}";
    }
}

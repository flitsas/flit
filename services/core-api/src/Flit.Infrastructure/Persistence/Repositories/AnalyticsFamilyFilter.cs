using System.Data.Common;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753) — «Ver por familia» en las lecturas analíticas (dashboard, Reportes,
/// reporte detallado y sus variantes de red): los conteos y filas excluyen los trámites cuya familia el usuario no puede
/// ver, con la misma lista que el listado de trámites (<c>IProcedureFamilyPermissionGate.ReadableFamilyCodes()</c>).
/// <para>La lista viaja como UN parámetro <c>text[]</c> (<c>@families</c>), nunca en el texto SQL. <c>NULL</c> = sin
/// restricción (flag apagado, SuperAdmin o las tres familias); arreglo vacío = ninguna familia visible (<c>= ANY('{}')</c>
/// no devuelve filas). La familia vive en el tipo (ADR-0050).</para>
/// <para>Cada repositorio declara un alias <c>const</c> local del predicado que usa (como <c>CategoriaCase</c>), para que
/// el barrido de interpolación SQL lo reconozca como fragmento constante del propio archivo.</para>
/// </summary>
internal static class AnalyticsFamilyFilter
{
    private const string ParameterName = "families";

    /// <summary>Predicado con el tipo ya unido con alias <c>pt</c>.</summary>
    internal const string ByType =
        "(@families::text[] IS NULL OR upper(pt.family) = ANY(@families::text[]))";

    /// <summary>Predicado sobre una instancia con alias <c>pi</c> (sin unir el tipo).</summary>
    internal const string ByInstance =
        "(@families::text[] IS NULL OR EXISTS (SELECT 1 FROM tramites.procedure_types ptf"
        + " WHERE ptf.id = pi.procedure_type_id AND upper(ptf.family) = ANY(@families::text[])))";

    /// <summary>Predicado sobre una transición del historial con alias <c>h</c>.</summary>
    internal const string ByHistory =
        "(@families::text[] IS NULL OR EXISTS (SELECT 1 FROM tramites.procedure_instances pif"
        + " JOIN tramites.procedure_types ptf ON ptf.id = pif.procedure_type_id"
        + " WHERE pif.id = h.procedure_instance_id AND upper(ptf.family) = ANY(@families::text[])))";

    /// <summary>Predicado sobre una fila de <c>analytics.v_procedure_detail_report</c> con alias <c>v</c>.</summary>
    internal const string ByDetailView =
        "(@families::text[] IS NULL OR EXISTS (SELECT 1 FROM tramites.procedure_types ptf"
        + " WHERE ptf.id = v.procedure_type_id AND upper(ptf.family) = ANY(@families::text[])))";

    /// <summary>Agrega <c>@families</c> tipado <c>text[]</c> (también cuando es <c>NULL</c>: sin tipo no se prepara).</summary>
    internal static void AddParam(DbCommand cmd, IReadOnlyList<string>? allowedFamilies)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = ParameterName;
        p.Value = allowedFamilies is null
            ? DBNull.Value
            : allowedFamilies.Select(f => f.Trim().ToUpperInvariant()).ToArray();
        if (p is NpgsqlParameter np)
            np.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text;
        cmd.Parameters.Add(p);
    }
}

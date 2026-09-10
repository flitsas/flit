namespace Flit.Modules.Quipux.Domain.Mapeo;

/// <summary>
/// Familias de trámite de la integración Quipux. Cada una corresponde a una bandera de la
/// secretaría destino en <c>catalogs.transit_offices</c>, de modo que un organismo puede estar
/// integrado para traspasos y no para matrículas — el alta es gradual.
/// </summary>
/// <remarks>
/// Son exactamente las tres de FLIT 1.0, donde <c>traffic_secretaries</c> tenía una columna por
/// familia: <c>id_parinttrasec_registration</c>, <c>id_parinttrasec_transfer</c> e
/// <c>id_parinttrasec_otherservice</c> (con el valor 2 = "integrada con Quipux").
/// <para>Vocabulario propio de la integración, deliberadamente separado de
/// <c>Flit.Tramites.Domain.Enums.ProcedureFamily</c>: son taxonomías de dos sistemas distintos y no
/// tienen por qué coincidir —la secretaría puede clasificar como «otros servicios» un trámite que
/// para FLIT es de matrículas—. La familia Quipux se declara en
/// <c>external_refs-&gt;'quipux'-&gt;&gt;'familia'</c>, junto al resto del mapeo, para que añadir un
/// trámite siga siendo un UPDATE.
/// <para>Antes esta separación se justificaba además porque <c>procedure_types.family</c> era un
/// dato poco fiable (convivían cuatro valores, <c>VEHICULAR</c> incluido, por seeds solapados).
/// ADR-0050 cerró eso con un CHECK de tres valores, así que el motivo que queda es el de arriba.
/// El DDL 83 verifica que las dos taxonomías no se contradigan en los casos donde sí deben
/// corresponderse.</para>
/// </para>
/// </remarks>
public static class QuipuxFamilia
{
    /// <summary>Matrículas. Bandera <c>transit_offices.quipux_registration</c>.</summary>
    public const string Matricula = "MATRICULA";

    /// <summary>Traspasos. Bandera <c>transit_offices.quipux_transfer</c>.</summary>
    public const string Traspaso = "TRASPASO";

    /// <summary>Resto de trámites. Bandera <c>transit_offices.quipux_other</c>.</summary>
    public const string Otros = "OTROS";

    public static readonly IReadOnlyList<string> Todas = [Matricula, Traspaso, Otros];

    /// <summary>
    /// ¿Es una familia conocida? Una familia desconocida en <c>external_refs</c> deja el tipo como
    /// no elegible: no habría bandera que consultar, y radicar sin saber si la secretaría está
    /// integrada para ese trámite es peor que no radicar.
    /// </summary>
    public static bool EsValida(string? familia) =>
        familia is not null && Todas.Contains(familia, StringComparer.Ordinal);
}

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #10989 (Feature #10972) — bloque de observaciones que declara el BENEFICIARIO del gravamen en
/// el FUR.
/// <para>Hasta esta HU el FUR solo marcaba la casilla <c>requested_process_11</c>: decía que había
/// prenda, pero no a favor de quién. El acreedor se capturaba en el wizard, se persistía, llegaba
/// hasta el generador como <c>FurDocumentData.AcreedorPrenda</c> y ahí se descartaba, porque el
/// mapper del FUR nunca lo referenciaba.</para>
/// <para>Por decisión D2 del plan técnico se imprime en el recuadro OBSERVACIONES, que ya es
/// multilínea y ya recibe texto automático, en vez de añadir un campo con coordenadas propias a las
/// tres plantillas (automotor / maquinaria / remolques).</para>
/// </summary>
public static class FurPrendaObservation
{
    /// <summary>Etiqueta del bloque. Constante para que el test la afirme sin duplicar el literal.</summary>
    public const string Etiqueta = "GRAVAMEN / PRENDA A FAVOR DE:";

    /// <summary>
    /// Devuelve el bloque de gravamen, o <c>null</c> si no hay nada que declarar.
    /// <para>Devuelve null cuando la decisión de prenda no implica gravamen (<paramref name="tienePrenda"/>
    /// falso) o cuando no se capturó el nombre del acreedor: <b>no se inventa contenido</b>. La casilla
    /// del FUR se marca igual por su propia vía; lo que se omite aquí es solo el texto.</para>
    /// <para>Si hay nombre pero no documento se imprime solo el nombre, sin guiones ni separadores
    /// sueltos que delaten un campo vacío.</para>
    /// </summary>
    public static string? Compose(bool tienePrenda, string? acreedorNombre, string? acreedorDocumento)
    {
        if (!tienePrenda)
            return null;

        var nombre = acreedorNombre?.Trim();
        if (string.IsNullOrEmpty(nombre))
            return null;

        var documento = acreedorDocumento?.Trim();
        return string.IsNullOrEmpty(documento)
            ? $"{Etiqueta} {nombre}"
            : $"{Etiqueta} {nombre} - NIT {documento}";
    }

    /// <summary>
    /// Une el bloque de gravamen con el resto de observaciones (manuales + automáticas de ADR-0029),
    /// anteponiéndolo. Cualquiera de los dos puede faltar; si faltan ambos devuelve <c>null</c> para
    /// que el recuadro quede exactamente como estaba antes de esta HU.
    /// </summary>
    public static string? Join(string? bloqueGravamen, string? resto)
    {
        var a = string.IsNullOrWhiteSpace(bloqueGravamen) ? null : bloqueGravamen.Trim();
        var b = string.IsNullOrWhiteSpace(resto) ? null : resto.Trim();

        if (a is null)
            return b;

        return b is null ? a : $"{a} {b}";
    }
}

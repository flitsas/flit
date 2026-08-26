using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #10989 (Feature #10972), ampliado por HU #11257 (Feature #11254, CF11) — bloque de observaciones
/// que declara el BENEFICIARIO del gravamen en el FUR, tanto para la constitución como para el
/// levantamiento.
/// <para>Hasta la HU #10989 el FUR solo marcaba la casilla <c>requested_process_11</c>: decía que había
/// prenda, pero no a favor de quién. El acreedor se capturaba en el wizard, se persistía, llegaba
/// hasta el generador como <c>FurDocumentData.AcreedorPrenda</c> y ahí se descartaba, porque el
/// mapper del FUR nunca lo referenciaba.</para>
/// <para>Hasta la HU #11257, un levantamiento no declaraba sobre qué prenda actuaba: <c>Compose</c>
/// recibía un <c>bool</c> que <c>levantar</c> colapsaba al mismo <c>false</c> que "sin prenda", así
/// que nunca emitía el literal de levantamiento. Ahora recibe la marca ya resuelta
/// (<see cref="FurPrendaMarking"/>) y compone el literal correspondiente a cada modalidad.</para>
/// <para>Por decisión D2 del plan técnico se imprime en el recuadro OBSERVACIONES, que ya es
/// multilínea y ya recibe texto automático, en vez de añadir un campo con coordenadas propias a las
/// tres plantillas (automotor / maquinaria / remolques).</para>
/// </summary>
public static class FurPrendaObservation
{
    /// <summary>Inscripción / registro de prenda (casilla 11).</summary>
    public const string Etiqueta = "Inscripción de prenda a favor de";

    /// <summary>Levantamiento de prenda (casilla 12) cuando solo se conoce el acreedor.</summary>
    public const string EtiquetaLevantamiento = "Levantamiento de prenda a favor de";

    /// <summary>
    /// Levantamiento de prenda declarando la entidad ante la que se hizo. Es el literal del trámite
    /// de levantamiento de prenda, donde el gestor sí captura ese dato.
    /// </summary>
    public const string EtiquetaLevantamientoEntidad = "Levantamiento de prenda ante";

    /// <summary>
    /// Devuelve el bloque de gravamen, o <c>null</c> si no hay nada que declarar.
    /// <para>Devuelve null cuando la marca es <see cref="FurPrendaMarking.Ninguna"/> o cuando no se
    /// capturó el nombre del acreedor: <b>no se inventa contenido</b>. La casilla del FUR se marca igual
    /// por su propia vía (<c>requested_process_11</c>/<c>_12</c>); lo que se omite aquí es solo el
    /// texto.</para>
    /// <para>Si hay nombre pero no documento se imprime solo el nombre, sin guiones ni separadores
    /// sueltos que delaten un campo vacío.</para>
    /// </summary>
    /// <param name="levantamientoEntidad">
    /// Entidad ante la que se extinguió el gravamen. Cuando viene, el bloque de levantamiento declara
    /// DÓNDE se hizo en vez de a favor de quién — el acreedor ya lo nombra el numeral 20 «A FAVOR DE»,
    /// y repetirlo en el recuadro gastaba renglones sin añadir información. Solo lo captura el trámite
    /// de levantamiento de prenda; en traspaso y matrícula llega <c>null</c> y el literal es el de
    /// siempre, así que esos dos flujos no cambian.
    /// </param>
    public static string? Compose(
        FurPrendaMarking marking,
        string? acreedorNombre,
        string? acreedorDocumento,
        string? levantamientoEntidad = null)
    {
        if (marking == FurPrendaMarking.Ambos)
        {
            return Join(
                Compose(FurPrendaMarking.Levantamiento, acreedorNombre, acreedorDocumento, levantamientoEntidad),
                Compose(FurPrendaMarking.Constitucion, acreedorNombre, acreedorDocumento));
        }

        if (marking == FurPrendaMarking.Levantamiento)
        {
            var entidad = levantamientoEntidad?.Trim();
            if (!string.IsNullOrEmpty(entidad))
                return $"{EtiquetaLevantamientoEntidad} {entidad}";
        }

        var etiqueta = marking switch
        {
            FurPrendaMarking.Constitucion => Etiqueta,
            FurPrendaMarking.Levantamiento => EtiquetaLevantamiento,
            _ => null,
        };
        if (etiqueta is null)
            return null;

        var nombre = acreedorNombre?.Trim();
        if (string.IsNullOrEmpty(nombre))
            return null;

        return $"{etiqueta} {nombre}";
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

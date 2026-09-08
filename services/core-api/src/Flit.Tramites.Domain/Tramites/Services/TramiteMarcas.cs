using Flit.Tramites.Domain.Documents;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// HU #12182 — las dos marcas informativas del listado de trámites: <b>prenda</b> y
/// <b>transformación</b>. Fuente única de las dos preguntas, para que la columna del listado, el
/// FUR y el mandato no puedan clasificar el mismo trámite de forma distinta.
///
/// <para><b>Cada marca tiene dos disparadores, y hacen falta los dos.</b> Un trámite puede llevar la
/// prenda o la transformación <i>encima</i> —una matrícula con gravamen, un traspaso que declara un
/// cambio de color— o <i>serlo</i>: desde ADR-0050, en la familia OTROS el cambio ES el trámite
/// (<c>CAMBIO_COLOR</c>, <c>BLINDAJE</c>, <c>PRENDA_INSCRIPCION</c>…). Mirar solo lo declarado
/// dejaría sin marca justo a los trámites que se llaman así, y mirar solo el tipo dejaría sin marca
/// a los ocho tipos de matrícula y traspaso, que es donde la marca más informa.</para>
/// </summary>
public static class TramiteMarcas
{
    /// <summary>
    /// Claves de <c>field_values</c> con las que el asistente declara una transformación
    /// (HU #11206: un texto afirmativo por transformación declarada). Son las cuatro capas que
    /// compone el objeto del mandato — ver <see cref="MandatoObjetoComposer"/>—, no tres: el
    /// blindaje es una transformación del vehículo como las otras.
    /// </summary>
    public static readonly IReadOnlyList<string> ClavesTransformacion =
    [
        MandatoObjetoComposer.CambioColor,
        MandatoObjetoComposer.CambioCarroceria,
        MandatoObjetoComposer.CambioCombustible,
        MandatoObjetoComposer.Blindaje,
    ];

    /// <summary>
    /// ¿El trámite declara o constituye una transformación del vehículo?
    /// </summary>
    /// <param name="fieldValues">Valores del formulario de la instancia (clave → texto).</param>
    /// <param name="tipoCodigo">Código del TIPO; un tipo de transformación marca por sí solo.</param>
    public static bool TieneTransformacion(
        IReadOnlyDictionary<string, string?> fieldValues, string? tipoCodigo)
    {
        if (ProcedureTypeLayers.EsTipoTransformacion(tipoCodigo))
            return true;

        foreach (var clave in ClavesTransformacion)
        {
            if (fieldValues.TryGetValue(clave, out var valor) && EsAfirmativo(valor))
                return true;
        }

        return false;
    }

    /// <summary>
    /// ¿El trámite tiene prenda? <paramref name="hayDecisionVigente"/> lo resuelve el repositorio en
    /// lote (la decisión vive en su propia tabla); aquí se le suma el tipo, porque un
    /// <c>LEVANTAMIENTO_PRENDA</c> es un trámite de prenda desde que se abre, antes de que nadie
    /// capture la decisión.
    /// </summary>
    public static bool TienePrenda(bool hayDecisionVigente, string? tipoCodigo) =>
        hayDecisionVigente || ProcedureTypeLayers.EsTipoPrendaBase(tipoCodigo);

    /// <summary>
    /// Un valor de formulario cuenta como afirmativo. Mismo criterio que la consulta de la empresa
    /// (<c>CompanyQueryRepository.EsAfirmativo</c>): el asistente guarda <c>"true"</c>, pero por los
    /// trámites migrados de V1 circulan también <c>1</c> y <c>si</c>.
    /// </summary>
    private static bool EsAfirmativo(string? value) =>
        value is not null && ValoresAfirmativos.Contains(value.Trim().ToLowerInvariant());

    /// <summary>
    /// Los mismos valores de <see cref="EsAfirmativo"/>, enumerables y ya en minúscula.
    ///
    /// <para>El filtro «Transformación» del listado (HU #12199) tiene que repetir esta comparación
    /// en SQL, donde no llega el método. Se expone la lista y el método se deriva de ella para que
    /// una no pueda quedarse corta respecto del otro: si aparece otra forma afirmativa en los
    /// migrados de V1, entra aquí y la reconocen los dos.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> ValoresAfirmativos = ["true", "1", "si", "sí"];
}

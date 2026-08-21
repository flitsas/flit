namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #11643 — decide QUÉ entra en el recuadro OBSERVACIONES del FUR cuando no cabe todo.
/// Orden y literales canónicos: <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c>.
///
/// <para><b>El problema.</b> El recuadro reúne cuatro bloques: el gravamen, el texto libre del gestor,
/// las transformaciones declaradas y el tipo de servicio con su empresa vinculadora. El texto libre no
/// tenía tope y se componía ANTES que los automáticos, así que al desbordar, lo que
/// <see cref="Flit.Infrastructure.Documents.Fur.FurTextFitter"/> eliminaba con la elipsis era
/// justamente la información obligatoria: el beneficiario del gravamen o la transformación declarada
/// desaparecían del formulario mientras sobrevivía un comentario del gestor.</para>
///
/// <para><b>La regla.</b> Lo automático va primero y entra COMPLETO; el texto libre va después y se
/// recorta a lo que quede. Es la prioridad correcta porque lo automático tiene consecuencias legales
/// (un gravamen no declarado en el FUR no queda declarado) mientras que el texto libre es apoyo.</para>
/// </summary>
public static class FurObservacionesComposer
{
    /// <summary>
    /// Caracteres que admite el recuadro sin que el auto-encaje tenga que truncar.
    ///
    /// <para>Medido con la fuente real sobre la geometría del manifiesto (w=392, h=33, cuerpo 6,5 con
    /// piso de 5): a 550 caracteres el texto aún entra completo y a 600 ya se recorta con elipsis. Se
    /// deja en 500 para no operar en el filo —el reparto en líneas depende de la longitud de las
    /// palabras reales, no del relleno con el que se midió— y porque por encima de 400 el cuerpo ya
    /// cae a 5 pt, que es el mínimo legible. Lo fija
    /// <c>FurObservacionesPresupuestoTests</c> midiendo con la fuente embebida, no por cálculo.</para>
    /// </summary>
    public const int PresupuestoCaracteres = 500;

    /// <summary>
    /// Une el bloque automático (íntegro) con el texto libre del gestor (recortado a lo que quede).
    /// Devuelve null si no hay nada que imprimir.
    /// </summary>
    public static string? Componer(string? automatico, string? manual)
    {
        var auto = automatico?.Trim();
        var libre = manual?.Trim();

        if (string.IsNullOrEmpty(auto))
            return string.IsNullOrEmpty(libre) ? null : Recortar(libre, PresupuestoCaracteres);

        if (string.IsNullOrEmpty(libre))
            return auto;

        // El bloque automático no se recorta NUNCA aquí: si por sí solo agota el presupuesto, el texto
        // libre desaparece entero y el auto-encaje del renderer se ocupa del resto. Cambiar esto
        // devolvería el defecto que la HU corrige.
        var disponible = PresupuestoCaracteres - auto.Length - 1; // -1 por el espacio separador
        if (disponible <= 0)
            return auto;

        var recortado = Recortar(libre, disponible);
        return string.IsNullOrEmpty(recortado) ? auto : $"{auto} {recortado}";
    }

    /// <summary>
    /// Recorta por límite de palabra y cierra con elipsis, para que el corte se vea. Si ni la primera
    /// palabra cabe, no se imprime nada: media palabra suelta confunde más que la ausencia.
    /// </summary>
    private static string Recortar(string texto, int maximo)
    {
        if (maximo <= 0) return string.Empty;
        if (texto.Length <= maximo) return texto;
        if (maximo <= 1) return string.Empty;

        var corte = texto.LastIndexOf(' ', Math.Min(maximo - 1, texto.Length - 1));
        return corte <= 0 ? string.Empty : $"{texto[..corte].TrimEnd()}…";
    }
}

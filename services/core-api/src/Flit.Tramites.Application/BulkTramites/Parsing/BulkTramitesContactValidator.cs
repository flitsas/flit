namespace Flit.Tramites.Application.BulkTramites.Parsing;

/// <summary>
/// Replica —sobre la fila ya parseada, antes de que exista el trámite— la obligatoriedad de los
/// datos de contacto que el paso de actores del wizard exige a CADA actor (correo, teléfono,
/// ciudad y dirección). Salió de las pruebas en DEV de la Feature #12519: una fila de empresa sin
/// ciudad ni dirección se reportaba «Creado» y, al abrir el trámite, el wizard lo devolvía a
/// Actores con «Vendedor pendiente» porque esos campos son obligatorios ahí. El lote no lo
/// detectaba porque <c>PutActorsHandler</c> los acepta vacíos; validarlos aquí deja la fila como
/// error estructural (igual que los porcentajes) y no se crea un trámite a medias.
///
/// <para>Aplica solo a los bloques de actor que traen <c>numero_documento</c>: los bloques vacíos
/// siguen siendo válidos. El código lleva el prefijo del actor (<c>datos_contacto_incompletos:vendedor_1</c>)
/// porque una fila de traspaso trae hasta 8 personas y hay que decir cuál.</para>
/// </summary>
public static class BulkTramitesContactValidator
{
    public const string DatosContactoIncompletos = "datos_contacto_incompletos";

    private static readonly string[] CamposObligatorios = ["email", "celular", "ciudad", "direccion"];

    /// <summary>Prefijos de bloque de actor que la plantilla puede traer.</summary>
    public static IReadOnlyList<string> PrefijosDe(BulkTramitesTemplateType tipo) => tipo switch
    {
        BulkTramitesTemplateType.Matricula => ["propietario_1", "propietario_2", "propietario_3", "propietario_4"],
        BulkTramitesTemplateType.Traspaso =>
        [
            "comprador_1", "comprador_2", "comprador_3", "comprador_4",
            "vendedor_1", "vendedor_2", "vendedor_3", "vendedor_4",
        ],
        BulkTramitesTemplateType.Otros => ["actor_1", "actor_2"],
        _ => [],
    };

    /// <summary>
    /// <c>null</c> si todos los actores con documento traen los cuatro datos de contacto; si no,
    /// <c>datos_contacto_incompletos:{prefijo}</c> del primer actor al que le falte alguno.
    /// </summary>
    public static string? Validate(BulkTramitesTemplateType tipo, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var prefijo in PrefijosDe(tipo))
        {
            if (ValueOrNull(values, $"{prefijo}_numero_documento") is null)
            {
                continue;
            }

            if (CamposObligatorios.Any(campo => ValueOrNull(values, $"{prefijo}_{campo}") is null))
            {
                return $"{DatosContactoIncompletos}:{prefijo}";
            }
        }

        return null;
    }

    private static string? ValueOrNull(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
}

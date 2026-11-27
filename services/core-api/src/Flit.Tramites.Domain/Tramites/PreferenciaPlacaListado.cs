using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;

namespace Flit.Tramites.Domain.Tramites;

/// <summary>
/// Épica #14503 / HU #14743 — datos de la preferencia de placa que el listado del gestor expone para que la fila
/// muestre el dígito pedido sin abrir el trámite. Se derivan de los field_values que el grafo del listado YA carga:
/// cero consultas extra (sin N+1) y cero llamadas al RUNT.
/// <para>Solo aplican a matrícula inicial (familia <see cref="ProcedureFamily.Matriculas"/>, el mismo criterio con el
/// que el resto del listado decide la modalidad); en cualquier otro trámite todo es <c>null</c>.</para>
/// </summary>
public readonly record struct PreferenciaPlacaListado(
    string? Digito,
    bool? Declarado,
    string? Clase,
    bool? RutaCorta)
{
    public const string DigitoKey = "plate_preferred_last_digit";
    public const string DeclaradoKey = "plate_preferred_last_digit_declared";
    public const string PlacaKey = "plate";
    public const string ClaseKey = "vehicle_class";

    /// <summary><c>source</c> de la placa que vino de la consulta al RUNT (ruta corta).</summary>
    public const string FuenteConsulta = "consultation";

    public static readonly PreferenciaPlacaListado Vacia = new(null, null, null, null);

    public static PreferenciaPlacaListado Desde(ProcedureFamily familia, IEnumerable<ProcedureInstanceFieldValue> fieldValues)
    {
        if (familia != ProcedureFamily.Matriculas)
            return Vacia;

        string? digito = null, declarado = null, clase = null;
        var rutaCorta = false;
        foreach (var f in fieldValues)
        {
            if (string.Equals(f.FieldKey, DigitoKey, StringComparison.OrdinalIgnoreCase))
                digito = f.ValueText;
            else if (string.Equals(f.FieldKey, DeclaradoKey, StringComparison.OrdinalIgnoreCase))
                declarado = f.ValueText;
            else if (string.Equals(f.FieldKey, ClaseKey, StringComparison.OrdinalIgnoreCase))
                clase = f.ValueText;
            else if (string.Equals(f.FieldKey, PlacaKey, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(f.ValueText)
                     && string.Equals(f.Source, FuenteConsulta, StringComparison.OrdinalIgnoreCase))
                rutaCorta = true;
        }

        bool? declaradoBool = string.Equals(declarado?.Trim(), "true", StringComparison.OrdinalIgnoreCase) ? true
            : string.Equals(declarado?.Trim(), "false", StringComparison.OrdinalIgnoreCase) ? false
            : null;

        return new PreferenciaPlacaListado(
            string.IsNullOrWhiteSpace(digito) ? null : digito.Trim(),
            declaradoBool,
            string.IsNullOrWhiteSpace(clase) ? null : clase,
            rutaCorta);
    }
}

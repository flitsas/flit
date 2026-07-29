namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Ciudad del organismo de tránsito para los documentos (HU #11016). El field_value
/// <c>transit_office_city</c> NO guarda el nombre del municipio sino su CÓDIGO DIVIPOLA (p. ej.
/// «25286»), porque así lo hidrata el preflight desde el catálogo de OT. Al imprimirlo, la solicitud
/// de trámite virtual salía como «25286, 28 de julio de 2026» y el código parecía pegado a la fecha.
/// <para>No hay catálogo código→nombre en el sistema, así que un valor puramente numérico se descarta
/// (los documentos imprimen solo la fecha) y un nombre real se sigue mostrando tal cual.</para>
/// </summary>
public static class TransitOfficeCity
{
    /// <summary>Nombre legible de la ciudad, o <c>null</c> si el valor es un código o está vacío.</summary>
    public static string? Legible(string? ciudad)
    {
        var valor = ciudad?.Trim();
        if (string.IsNullOrEmpty(valor))
            return null;

        return valor.All(char.IsDigit) ? null : valor;
    }
}

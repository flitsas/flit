namespace Flit.Admin.Application.Companies.Settings;

/// <summary>
/// Selección de proveedor de consulta RUNT para un tipo (HU #10478), usada en el request y la
/// respuesta de configuración. Claves JSON: <c>primary</c>, <c>fallback</c>. En el request los campos
/// pueden llegar nulos/vacíos y se validan en el handler; en la respuesta siempre vienen poblados.
/// </summary>
public sealed record ConsultationProviderChoice(
    string? Primary,
    IReadOnlyList<string>? Fallback);

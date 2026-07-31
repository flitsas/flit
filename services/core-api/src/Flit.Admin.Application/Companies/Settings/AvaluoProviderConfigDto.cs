namespace Flit.Admin.Application.Companies.Settings;

/// <summary>
/// Configuración de proveedores de avalúo del tenant (Feature #10707), usada en el request y la
/// respuesta de configuración. Claves JSON: <c>primary</c> (proveedor sugerido) y <c>enabled</c>
/// (proveedores habilitados). En el request pueden llegar nulos y se validan en el handler; en la
/// respuesta siempre vienen poblados (Fasecolda siempre presente).
/// </summary>
public sealed record AvaluoProviderConfigDto(
    string? Primary,
    IReadOnlyList<string>? Enabled);

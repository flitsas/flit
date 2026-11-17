namespace Flit.Consultas.Api.RuntIntempo;

/// <summary>
/// Mapa de tipo de documento de Intempo (HU #13874, especificación de migración §6.1). Puerto de
/// <c>IntempoApiProxy.typeDocumentMap</c> (BackCrudTransfer_master <c>src/infrastructure/proxies/intempoApiProxy.ts</c>).
/// Se aplica a <c>documentType</c>/<c>documentNumber</c> de la raíz, a <c>owner[].documentType</c> y a
/// <c>lessee[].documentType</c>; no a <c>propietariosActuales</c> ni a <c>historicoPropietarios</c>.
/// </summary>
internal static class MapaTipoDocumentoIntempo
{
    private static readonly Dictionary<string, string> Mapa = new(StringComparer.Ordinal)
    {
        ["C"] = "Cédula de ciudadanía",
        ["CC"] = "Cédula de ciudadanía",
        ["N"] = "NIT",
        ["NIT"] = "NIT",
        ["X"] = "N.N.",
        ["E"] = "Cédula de extranjería",
        ["P"] = "Pasaporte",
        ["T"] = "Tarjeta de identidad",
        ["U"] = "N.U.I.P.",
        ["D"] = "Carne Diplomático",
    };

    /// <summary>
    /// Texto del código; si la clave no está, el valor original; si es nulo, <c>""</c>
    /// (<c>typeDocumentMap[v] || v || ''</c>). Distingue mayúsculas, como el original.
    /// </summary>
    public static string Mapear(string? valor) =>
        valor is null ? string.Empty : Mapa.TryGetValue(valor, out var texto) ? texto : valor;
}

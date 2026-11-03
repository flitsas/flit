using System.Text.Json;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Licencias de conducción a partir del JSON del conductor (Kyverum <c>licencias</c> / Verifik <c>licenses</c>),
/// incluidas las que no están activas. No copia nombres de proveedor ni otros campos del cuerpo.
/// </summary>
internal static class LicenciasConductor
{
    public static IReadOnlyList<LicenciaDetalle> Leer(JsonElement raiz)
    {
        var lista = Lista(raiz) ?? ListaEn(raiz, "data") ?? ListaEn(raiz, "resultado");
        if (lista is not { } arreglo)
            return [];

        var filas = new List<LicenciaDetalle>();
        foreach (var item in arreglo.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var numero = Texto(item, "numeroLicencia", "licenceNumber", "numero", "numero_licencia");
            var estado = Texto(item, "estadoLicencia", "status", "estado");
            var detalle = Arreglo(item, "detalleLicencia") ?? Arreglo(item, "detalle_licencia");
            if (detalle is { } categorias && categorias.GetArrayLength() > 0)
            {
                foreach (var categoria in categorias.EnumerateArray())
                {
                    if (categoria.ValueKind != JsonValueKind.Object)
                        continue;
                    var nombre = Texto(categoria, "categoria", "category");
                    var hasta = Texto(categoria, "fechaVencimiento", "dueDate", "vigente_hasta", "vigenteHasta");
                    if (nombre is null && numero is null)
                        continue;
                    filas.Add(new LicenciaDetalle(nombre, estado, hasta, numero));
                }
            }
            else
            {
                var nombre = Texto(item, "categoria", "category");
                var hasta = Texto(item, "fechaVencimiento", "dueDate", "vigente_hasta", "vigenteHasta");
                if (nombre is null && numero is null && estado is null)
                    continue;
                filas.Add(new LicenciaDetalle(nombre, estado, hasta, numero));
            }
        }

        return filas;
    }

    private static JsonElement? Lista(JsonElement objeto) =>
        Arreglo(objeto, "licencias") ?? Arreglo(objeto, "licenses");

    private static JsonElement? ListaEn(JsonElement objeto, string propiedad) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(propiedad, out var hijo) ? Lista(hijo) : null;

    private static JsonElement? Arreglo(JsonElement objeto, string propiedad) =>
        objeto.ValueKind == JsonValueKind.Object
        && objeto.TryGetProperty(propiedad, out var valor)
        && valor.ValueKind == JsonValueKind.Array
            ? valor
            : null;

    private static string? Texto(JsonElement objeto, params string[] propiedades)
    {
        foreach (var propiedad in propiedades)
        {
            if (objeto.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String)
            {
                var texto = valor.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(texto))
                    return texto;
            }
        }

        return null;
    }
}

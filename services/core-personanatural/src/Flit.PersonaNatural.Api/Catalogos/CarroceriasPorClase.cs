using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Flit.PersonaNatural.Api.Catalogos;

/// <summary>
/// HU #14255 — carrocerías permitidas por clase de vehículo, con la MISMA regla que el asistente de FLIT 2
/// (<c>frontend/lib/catalogs/bodywork-by-class.ts</c> → <c>getBodyworksForVehicleClass</c>). La fuente es una copia embebida de
/// <c>bodywork-by-class.json</c> (carroceria.xlsx): core-api no tiene este catálogo por clase en un servicio que el BFF pueda
/// llamar con la misma precisión, y duplicar la lista en el navegador es lo que se quiere evitar. Una prueba compara la copia
/// contra el archivo de FLIT 2 para detectar divergencias. Sin clase, o sin coincidencia, la lista es VACÍA (no se lista todo).
/// </summary>
public static class CarroceriasPorClase
{
    /// <summary>Variantes ortográficas reales entre la clase del RUNT y la clave del catálogo (no cubiertas por quitar tildes).</summary>
    private static readonly Dictionary<string, string> Alias = new(StringComparer.Ordinal) { ["SEMIRREMOLQUE"] = "SEMIREMOLQUE" };

    private sealed record Fila(string Code, string Name, string[] Classes);

    private static readonly Lazy<(Dictionary<string, List<Fila>> PorClase, List<Fila> Todas, Dictionary<string, string> Indice)> Catalogo = new(Cargar);

    private static (Dictionary<string, List<Fila>>, List<Fila>, Dictionary<string, string>) Cargar()
    {
        using var flujo = typeof(CarroceriasPorClase).Assembly.GetManifestResourceStream("bodywork-by-class.json")
            ?? throw new InvalidOperationException("Falta el recurso bodywork-by-class.json.");
        using var doc = JsonDocument.Parse(flujo);
        var porClase = new Dictionary<string, List<Fila>>(StringComparer.Ordinal);
        foreach (var clase in doc.RootElement.GetProperty("byVehicleClass").EnumerateObject())
            porClase[clase.Name] = [.. clase.Value.EnumerateArray().Select(e => new Fila(e.GetProperty("code").GetString() ?? string.Empty, e.GetProperty("name").GetString() ?? string.Empty, []))];

        var todas = new List<Fila>();
        if (doc.RootElement.TryGetProperty("allBodyworks", out var all))
        {
            foreach (var e in all.EnumerateArray())
            {
                var clases = e.TryGetProperty("classes", out var c) ? c.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray() : [];
                todas.Add(new Fila(e.GetProperty("code").GetString() ?? string.Empty, e.GetProperty("name").GetString() ?? string.Empty, clases));
            }
        }

        var indice = porClase.Keys.ToDictionary(Normalizar, k => k, StringComparer.Ordinal);
        return (porClase, todas, indice);
    }

    /// <summary>Trim + mayúsculas + sin tildes + espacios colapsados (igual que <c>normalizeVehicleClass</c>).</summary>
    public static string Normalizar(string? clase)
    {
        // Mapa explícito (no Normalize): no depende de la globalización del host (la descomposición Unicode puede no estar disponible).
        var sb = new StringBuilder();
        foreach (var c in (clase ?? string.Empty).Trim().ToUpperInvariant())
            sb.Append(c switch
            {
                'Á' or 'À' or 'Ä' or 'Â' => 'A',
                'É' or 'È' or 'Ë' or 'Ê' => 'E',
                'Í' or 'Ì' or 'Ï' or 'Î' => 'I',
                'Ó' or 'Ò' or 'Ö' or 'Ô' => 'O',
                'Ú' or 'Ù' or 'Ü' or 'Û' => 'U',
                'Ñ' => 'N',
                _ => c,
            });
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? ResolverClave(string normalizada, Dictionary<string, string> indice)
    {
        if (normalizada.Length == 0)
            return null;
        if (indice.TryGetValue(normalizada, out var directa))
            return directa;
        if (Alias.TryGetValue(normalizada, out var alias) && indice.TryGetValue(alias, out var viaAlias))
            return viaAlias;
        // El RUNT a veces manda un calificativo («CAMION CISTERNA»): la clave más larga que sea la clase o su prefijo de palabra.
        var acierto = indice.Keys.OrderByDescending(k => k.Length).FirstOrDefault(k => normalizada == k || normalizada.StartsWith(k + " ", StringComparison.Ordinal));
        return acierto is null ? null : indice[acierto];
    }

    /// <summary>Carrocerías de la clase (sin repetir nombre), en el orden del catálogo; vacío si no hay clase o no coincide.</summary>
    public static IReadOnlyList<ItemCatalogoDto> Para(string? clase, string? texto = null)
    {
        var (porClase, todas, indice) = Catalogo.Value;
        var clave = ResolverClave(Normalizar(clase), indice);
        if (clave is null)
            return [];

        var quiero = Normalizar(clave);
        var etiquetadas = todas.Where(f => f.Classes.Any(c => Normalizar(c) == quiero)).ToList();
        var origen = etiquetadas.Count > 0 ? etiquetadas : porClase[clave];
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var salida = new List<ItemCatalogoDto>();
        var filtro = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        foreach (var fila in origen)
        {
            var nombre = fila.Name.Trim();
            if (nombre.Length == 0 || !vistos.Add(nombre.ToUpperInvariant()))
                continue;
            if (filtro is not null && !nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase))
                continue;
            // El valor que guarda FLIT 2 en vehicle_body_type es el nombre.
            salida.Add(new ItemCatalogoDto(nombre, nombre));
        }

        return salida;
    }
}

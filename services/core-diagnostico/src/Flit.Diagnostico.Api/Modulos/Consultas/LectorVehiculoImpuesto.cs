using System.Text.Json;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Clase, marca, servicio y combustible del vehículo según <c>informacion_general</c> del RUNT (D3), en memoria.
/// No se persiste. Acepta snake_case y camelCase.
/// </summary>
internal static class LectorVehiculoImpuesto
{
    public sealed record VehiculoRunt(string? Clase, string? Marca, string? Servicio, string? Combustible);

    public static VehiculoRunt? Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var respuesta = JsonDocument.Parse(json);
            if (!Propiedad(respuesta.RootElement, "vehiculo", "vehiculo", out var vehiculo))
                return null;
            if (!Propiedad(vehiculo, "informacion_general", "informacionGeneral", out var general) || general.ValueKind != JsonValueKind.Object)
                return null;

            var clase = Limpio(Texto(general, "clase_vehiculo", "claseVehiculo"));
            var marca = Limpio(Texto(general, "marca", "marca"));
            var servicio = Limpio(Texto(general, "tipo_servicio", "tipoServicio"));
            var combustible = Limpio(Texto(general, "tipo_combustible", "tipoCombustible"));
            return clase is null && marca is null && servicio is null && combustible is null
                ? null
                : new VehiculoRunt(clase, marca, servicio, combustible);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Limpio(string? valor)
    {
        var limpio = valor?.Trim();
        return string.IsNullOrEmpty(limpio) ? null : limpio;
    }

    private static string? Texto(JsonElement elemento, string snake, string camel) =>
        Propiedad(elemento, snake, camel, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static bool Propiedad(JsonElement elemento, string snake, string camel, out JsonElement valor)
    {
        if (elemento.ValueKind == JsonValueKind.Object && (elemento.TryGetProperty(snake, out valor) || elemento.TryGetProperty(camel, out valor)))
            return true;
        valor = default;
        return false;
    }
}

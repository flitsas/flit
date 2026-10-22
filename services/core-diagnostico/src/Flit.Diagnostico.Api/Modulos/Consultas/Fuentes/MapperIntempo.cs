using System.Text.Json;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Mapper del producto para la respuesta de Intempo (HU #13725, H-INT): el DTO de core-api solo mapea
/// <c>propietariosActuales</c>; este hidrata además <c>locatarios[]</c> de cada propietario, <c>lessee[]</c> y
/// <c>tipoPropiedad</c>/<c>detallePropiedad</c> y, para el avalúo (HU #13795), marca, línea, modelo, cilindraje,
/// <c>tipoCombustible</c> y pasajeros de <c>informacionGeneral</c>. Toma solo tipo y número de documento: los nombres (<c>nombreCompleto</c>,
/// <c>nombreRazonSocial</c>, <c>names</c>) nunca salen de aquí. Una respuesta que no es JSON, que no trae
/// <c>informacionGeneral</c> o cuyo <c>codigoResultado</c> no es «Exitoso» es <see cref="TipoErrorFuente.RespuestaInvalida"/>.
/// </summary>
/// <example>
/// <code>
/// var resultado = MapperIntempo.Mapear(cuerpo, "INTEMPO");
/// // resultado.Vehiculo.Propietarios[0] → ("NIT", "999990003", Leasing, [("CC", "9999900013")])
/// </code>
/// </example>
public static class MapperIntempo
{
    public static ResultadoRunt Mapear(string json, string servicioQueResolvio)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Invalida("la respuesta de Intempo no es JSON válido");
        }

        using (documento)
        {
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
                return Invalida("la respuesta de Intempo no es un objeto");
            if (!string.Equals(Texto(raiz, "codigoResultado"), "Exitoso", StringComparison.OrdinalIgnoreCase))
                return Invalida("Intempo respondió sin éxito");
            if (!raiz.TryGetProperty("informacionGeneral", out var general) || general.ValueKind != JsonValueKind.Object)
                return Invalida("la respuesta de Intempo no trae informacionGeneral");

            var propietarios = Arreglo(raiz, "propietariosActuales")
                .Select(Propietario)
                .OfType<PropietarioRunt>()
                .ToList();
            var locatarios = Arreglo(raiz, "lessee")
                .Select(l => Locatario(l, "documentType", "documentNumber"))
                .Concat(propietarios.SelectMany(p => p.Locatarios))
                .OfType<LocatarioRunt>()
                .Distinct()
                .ToList();

            return ResultadoRunt.Exito(new VehiculoRunt(
                Placa: Texto(general, "noPlaca"),
                Vin: Texto(general, "noVin"),
                Servicio: Texto(general, "tipoServicio"),
                Clase: Texto(general, "claseVehiculo"),
                Organismo: Texto(general, "organismoTransito"),
                Propietarios: propietarios,
                Locatarios: locatarios,
                ServicioQueResolvio: servicioQueResolvio,
                IdTransaccion: Texto(raiz, "codigoUUID"))
            {
                // HU #13795: datos técnicos del bien para el avalúo comercial.
                Marca = Texto(general, "marca"),
                Linea = Texto(general, "linea"),
                Modelo = Entero(general, "modelo"),
                Cilindraje = Entero(general, "cilindraje"),
                Combustible = Texto(general, "tipoCombustible"),
                Pasajeros = Entero(general, "pasajeros") ?? Entero(general, "pasajerosSentados") ?? Entero(general, "capacidadPasajeros"),
            });
        }
    }

    /// <summary>Código del tipo de documento del producto (CC, CE, NIT, PAS o TI) para el código de Intempo (C, N, …).</summary>
    public static string TipoDocumento(string codigo) => codigo.Trim().ToUpperInvariant() switch
    {
        "C" or "CC" => "CC",
        "N" or "NIT" => "NIT",
        "E" or "CE" => "CE",
        "P" or "PA" or "PAS" => "PAS",
        "T" or "TI" => "TI",
        var otro => otro,
    };

    private static PropietarioRunt? Propietario(JsonElement p)
    {
        var tipo = Texto(p, "tipoDocumento");
        var numero = Texto(p, "noDocumento");
        if (tipo is null || numero is null)
            return null;
        var locatarios = Arreglo(p, "locatarios")
            .Select(l => Locatario(l, "tdoc", "ndoc"))
            .OfType<LocatarioRunt>()
            .Distinct()
            .ToList();
        return new PropietarioRunt(TipoDocumento(tipo), numero, Propiedad(p), locatarios);
    }

    private static LocatarioRunt? Locatario(JsonElement l, string claveTipo, string claveNumero)
    {
        var tipo = Texto(l, claveTipo);
        var numero = Texto(l, claveNumero);
        return tipo is null || numero is null ? null : new LocatarioRunt(TipoDocumento(tipo), numero);
    }

    private static TipoPropiedad Propiedad(JsonElement p) => Texto(p, "detallePropiedad")?.ToUpperInvariant() switch
    {
        "PROPIO" => TipoPropiedad.Propio,
        "LEASING" => TipoPropiedad.Leasing,
        _ => Texto(p, "tipoPropiedad") switch
        {
            "1" => TipoPropiedad.Propio,
            "3" => TipoPropiedad.Leasing,
            _ => TipoPropiedad.Desconocido,
        },
    };

    private static List<JsonElement> Arreglo(JsonElement objeto, string clave) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList()
            : [];

    /// <summary>Escalar como texto sin espacios en los extremos (Intempo manda cifras como string o como número); null si vacío.</summary>
    private static string? Texto(JsonElement objeto, string clave)
    {
        if (objeto.ValueKind != JsonValueKind.Object || !objeto.TryGetProperty(clave, out var v))
            return null;
        var texto = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }

    /// <summary>Entero positivo de un escalar (texto o número, admite «1598.0»); null si no viene o no es un número.</summary>
    private static int? Entero(JsonElement objeto, string clave)
    {
        var texto = Texto(objeto, clave);
        if (texto is null || !decimal.TryParse(texto, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var valor))
            return null;
        return valor is > 0 and <= int.MaxValue ? (int)valor : null;
    }

    private static ResultadoRunt Invalida(string motivo) => ResultadoRunt.Fallo(TipoErrorFuente.RespuestaInvalida, $"RUNT (Intempo): {motivo}.");
}

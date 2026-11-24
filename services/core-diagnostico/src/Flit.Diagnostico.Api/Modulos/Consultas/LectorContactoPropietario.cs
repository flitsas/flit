using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Contacto del propietario dueño del documento de la solicitud, leído del RUNT en memoria (D3). Misma persona que
/// <see cref="LectorNombrePropietario"/>; teléfono, correo, departamento, ciudad y dirección salen de la misma entrada
/// de <c>direcciones[]</c> (la de <c>fecha_actualizacion</c> más reciente; sin fechas, la primera con dirección). Lo
/// inválido queda null. Dos propietarios con el mismo documento y contactos distintos → todo null. No se persiste.
/// </summary>
internal static partial class LectorContactoPropietario
{
    public sealed record ContactoRunt(string? Telefono, string? Correo, string? Departamento, string? Ciudad, string? Direccion);

    public static ContactoRunt? Leer(string? json, DocumentoPersona documento)
    {
        ArgumentNullException.ThrowIfNull(documento);
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var respuesta = JsonDocument.Parse(json);
            if (!Propiedad(respuesta.RootElement, "vehiculo", "vehiculo", out var vehiculo))
                return null;
            if (!Propiedad(vehiculo, "propietarios_actuales", "propietariosActuales", out var lista) || lista.ValueKind != JsonValueKind.Array)
                return null;

            ContactoRunt? hallado = null;
            foreach (var propietario in lista.EnumerateArray())
            {
                if (!LectorNombrePropietario.EsDe(propietario, documento))
                    continue;
                var contacto = De(propietario);
                if (hallado is not null && !Mismo(hallado, contacto))
                    return null;
                hallado = contacto;
            }

            return hallado;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ContactoRunt De(JsonElement propietario)
    {
        if (!Propiedad(propietario, "direcciones", "direcciones", out var direcciones) || direcciones.ValueKind != JsonValueKind.Array)
            return new ContactoRunt(null, null, null, null, null);

        JsonElement? elegida = null;
        DateTime? mejorFecha = null;
        JsonElement? primeraConDireccion = null;
        foreach (var entrada in direcciones.EnumerateArray())
        {
            if (entrada.ValueKind != JsonValueKind.Object)
                continue;
            var direccion = Texto(entrada, "direccion", "direccion");
            if (primeraConDireccion is null && !string.IsNullOrWhiteSpace(direccion))
                primeraConDireccion = entrada;
            var fecha = Fecha(Texto(entrada, "fecha_actualizacion", "fechaActualizacion"));
            if (fecha is null)
                continue;
            if (mejorFecha is null || fecha > mejorFecha)
            {
                mejorFecha = fecha;
                elegida = entrada;
            }
        }

        elegida ??= primeraConDireccion;
        if (elegida is null)
            return new ContactoRunt(null, null, null, null, null);

        var e = elegida.Value;
        return new ContactoRunt(
            Telefono(Texto(e, "telefono", "telefono")),
            Correo(Texto(e, "email", "email") ?? Texto(e, "correo", "correo")),
            Limpio(Texto(e, "departamento", "departamento")),
            Limpio(Texto(e, "ciudad", "ciudad") ?? Texto(e, "municipio", "municipio")),
            Direccion(Texto(e, "direccion", "direccion")));
    }

    private static bool Mismo(ContactoRunt a, ContactoRunt b) =>
        string.Equals(a.Telefono, b.Telefono, StringComparison.Ordinal)
        && string.Equals(a.Correo, b.Correo, StringComparison.Ordinal)
        && string.Equals(a.Departamento, b.Departamento, StringComparison.Ordinal)
        && string.Equals(a.Ciudad, b.Ciudad, StringComparison.Ordinal)
        && string.Equals(a.Direccion, b.Direccion, StringComparison.Ordinal);

    private static string? Telefono(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;
        var digitos = SoloDigitos().Replace(valor, string.Empty);
        return digitos.Length is >= 7 and <= 10 ? digitos : null;
    }

    private static string? Correo(string? valor)
    {
        var limpio = Limpio(valor);
        return limpio is { Length: > 0 and <= 120 } && limpio.Contains('@', StringComparison.Ordinal) ? limpio : null;
    }

    private static string? Direccion(string? valor)
    {
        var limpio = Limpio(valor);
        return limpio is { Length: > 0 and <= 200 } ? limpio : null;
    }

    private static string? Limpio(string? valor)
    {
        var limpio = valor?.Trim();
        return string.IsNullOrEmpty(limpio) ? null : limpio;
    }

    private static DateTime? Fecha(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        if (DateTime.TryParseExact(texto.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return local;
        return DateTime.TryParse(texto.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var iso)
            ? iso
            : null;
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

    [GeneratedRegex(@"\D")]
    private static partial Regex SoloDigitos();
}

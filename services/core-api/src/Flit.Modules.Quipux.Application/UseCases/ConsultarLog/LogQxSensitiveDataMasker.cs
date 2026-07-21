using System.Buffers;
using System.Text.Json;

namespace Flit.Modules.Quipux.Application.UseCases.ConsultarLog;

/// <summary>
/// Enmascarado de datos sensibles del <c>detail</c> de un evento del LOG QX (HU #10794). Es una
/// SEGUNDA BARRERA sobre el jsonb que ya viene sanitizado desde captura: recorre el detail y, para
/// cualquier clave cuyo nombre sugiera PII (documento, cédula, NIT, correo, teléfono, nombre,
/// dirección, propietario…), enmascara su valor dejando solo los últimos 4 caracteres. Así, aunque
/// un dato sensible se filtrara al detail pese a la sanitización, nunca se devuelve en crudo.
/// </summary>
/// <remarks>
/// Se aplica solo al <c>detail</c> técnico (donde podría colarse un dato del propietario), no a los
/// campos de cabecera de la radicación (razón social de la compañía, placa) que son datos de negocio
/// que la pantalla muestra a propósito. Las claves técnicas de la instrumentación
/// (<c>codigo</c>, <c>duration_ms</c>, <c>origen</c>, banderas, ids) no casan con la lista sensible y
/// pasan intactas.
/// </remarks>
internal static class LogQxSensitiveDataMasker
{
    private const int VisibleTail = 4;

    /// <summary>
    /// Fragmentos que marcan una clave como sensible (match case-insensitive por subcadena). Se cubren
    /// las variantes con y sin tilde porque el jsonb puede traer cualquiera.
    /// </summary>
    private static readonly string[] SensitiveKeyFragments =
    [
        "documento", "document", "cedula", "cédula", "identificacion", "identificación",
        "nit", "correo", "email", "mail", "telefono", "teléfono", "celular", "phone",
        "propietario", "nombre", "apellido", "direccion", "dirección", "address",
    ];

    /// <summary>
    /// Devuelve una copia del <paramref name="source"/> con los valores de claves sensibles
    /// enmascarados. Estructura, orden de claves y valores no sensibles se preservan verbatim.
    /// </summary>
    public static JsonElement Mask(JsonElement source)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteMasked(writer, source, maskValues: false);
        }

        using var doc = JsonDocument.Parse(buffer.WrittenMemory);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Reescribe <paramref name="element"/> en <paramref name="writer"/>. Cuando
    /// <paramref name="maskValues"/> es true (heredado de una clave sensible) los escalares string y
    /// number se enmascaran; booleanos y null se dejan como están (no son PII).
    /// </summary>
    private static void WriteMasked(Utf8JsonWriter writer, JsonElement element, bool maskValues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    // La sensibilidad se decide por el nombre de la clave y se propaga hacia adentro:
                    // un objeto/array bajo una clave sensible enmascara todos sus escalares.
                    WriteMasked(writer, property.Value, maskValues || IsSensitiveKey(property.Name));
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteMasked(writer, item, maskValues);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                if (maskValues)
                {
                    writer.WriteStringValue(MaskScalar(element.GetString()));
                }
                else
                {
                    writer.WriteStringValue(element.GetString());
                }

                break;

            case JsonValueKind.Number:
                if (maskValues)
                {
                    // Un número sensible (p. ej. documento como entero) se degrada a string enmascarada.
                    writer.WriteStringValue(MaskScalar(element.GetRawText()));
                }
                else
                {
                    writer.WriteRawValue(element.GetRawText());
                }

                break;

            default:
                // True | False | Null | Undefined: no son PII, se copian tal cual.
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitiveKey(string key) =>
        SensitiveKeyFragments.Any(fragment =>
            key.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>Deja visibles solo los últimos <see cref="VisibleTail"/> caracteres.</summary>
    private static string? MaskScalar(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length <= VisibleTail)
        {
            return new string('*', value.Length);
        }

        return string.Concat(
            new string('*', value.Length - VisibleTail),
            value.AsSpan(value.Length - VisibleTail));
    }
}

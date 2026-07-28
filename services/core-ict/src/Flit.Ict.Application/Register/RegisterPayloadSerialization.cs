using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flit.Ict.Application.Register;

/// <summary>
/// Convierte a <see cref="string"/> tolerando que el cliente envíe un número o booleano donde el
/// contrato v1 documenta String. El backend Node/TSOA de v1 era laxo con los tipos (p.ej. el ejemplo
/// del contrato manda <c>traffic_secretary_code</c> como número <c>99001001</c>); se replica esa
/// tolerancia para NO romper clientes existentes que copian el ejemplo del contrato.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            // Número: los campos String del contrato que llegan como número (códigos, documentos,
            // teléfonos) son enteros → se leen exactos como Int64; el resto como decimal invariante.
            JsonTokenType.Number => reader.TryGetInt64(out var l)
                ? l.ToString(CultureInfo.InvariantCulture)
                : reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException($"No se puede convertir un token {reader.TokenType} a String."),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>
/// Opciones de deserialización del payload de registro v1: tolerantes con los tipos (números donde se
/// espera String) para calcar la laxitud del backend v1. Se aplican SOLO al endpoint de registro.
/// </summary>
public static class RegisterPayloadSerialization
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new FlexibleStringConverter());
        return options;
    }
}

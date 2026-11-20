using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// La respuesta COMPLETA de la fuente guardada junto a la celda (HU #14087 AC8; decisión del usuario 2026-10-27, enmienda de
/// ADR-0075 en Propuesto): <see cref="CeldaConsulta.RespuestaFuenteCifrada"/>, cifrada con <see cref="IProtectorRespuestaFuente"/>.
/// No reemplaza ni toca el <c>payload</c> normalizado (tablero, PDF, Excel, dictamen y reutilización siguen leyendo ése).
/// <list type="bullet">
/// <item>Fuentes por vehículo: el texto exacto del proto-JSON que llegó en <c>consultas.resultado.diagnostico</c>; null si el
/// resultado fue ERROR (sin respuesta).</item>
/// <item>Fuentes de persona: un objeto <c>{"&lt;orden&gt;": "&lt;proto-JSON&gt;"}</c> con la respuesta de cada persona del rol por su
/// orden (la reutilización del mismo día copia los bytes tal cual porque casa las personas por rol y orden). Al volver a
/// publicar, al cerrar sin consulta y en el llenado manual solo quedan las personas que respondieron OK
/// (<see cref="SoloRespondidas"/>), igual que en el payload.</item>
/// </list>
/// Un cifrado que no se puede leer nunca tumba el flujo: al escribir se empieza de cero; al leer (<see cref="TryLeerVehiculo"/>,
/// <see cref="TryLeerPersonas"/>) se informa con <c>false</c> y quien lee decide (el detalle responde <c>null</c>).
/// </summary>
/// <example>
/// <code>
/// resultado = resultado with
/// {
///     RespuestaFuenteCifrada = RespuestaFuenteCelda.AlAplicar(protector, celda.Fuente, celda.RespuestaFuenteCifrada, data.PersonaId,
///         RespuestaFuenteCelda.Cruda(data), resultado.Payload),
/// };
/// </code>
/// </example>
internal static class RespuestaFuenteCelda
{
    /// <summary>El texto exacto de la respuesta del bus; null si el resultado es ERROR o la respuesta no es un objeto.</summary>
    public static string? Cruda(ResultadoConsultaDiagnostico data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return string.Equals(data.Estado, ResultadoConsultaDiagnostico.EstadoOk, StringComparison.OrdinalIgnoreCase)
            && data.Respuesta is { ValueKind: JsonValueKind.Object } respuesta
            ? respuesta.GetRawText()
            : null;
    }

    /// <summary>
    /// La columna tras aplicar una respuesta. Por vehículo: <paramref name="cruda"/> cifrada (null sin respuesta). De persona: el
    /// objeto de <paramref name="actual"/> con la respuesta de <paramref name="personaId"/> bajo su orden (leído de
    /// <paramref name="payloadResultado"/>, el payload que deja el ejecutor) o sin ella si no trae respuesta; si la persona no está
    /// en ese payload (ya no es del rol), <paramref name="actual"/> sin cambios.
    /// </summary>
    public static byte[]? AlAplicar(
        IProtectorRespuestaFuente protector, Fuente fuente, byte[]? actual, Guid? personaId, string? cruda, string? payloadResultado)
    {
        ArgumentNullException.ThrowIfNull(protector);
        if (!EjecutorPorPersonas.EsPorPersona(fuente))
            return cruda is null ? null : protector.Proteger(cruda);

        var persona = personaId is { } id ? EjecutorPorPersonas.Leer(payloadResultado)?.Personas.FirstOrDefault(p => p.PersonaId == id) : null;
        if (persona is null)
            return actual;
        var mapa = LeerMapa(protector, actual) ?? [];
        var clave = Clave(persona.Orden);
        if (cruda is null)
            mapa.Remove(clave);
        else
            mapa[clave] = cruda;
        return Proteger(protector, mapa);
    }

    /// <summary>
    /// Solo las respuestas de las personas que están OK en <paramref name="payload"/> (null si no queda ninguna); <paramref name="actual"/>
    /// tal cual si no cambia nada (no se vuelve a cifrar).
    /// </summary>
    public static byte[]? SoloRespondidas(IProtectorRespuestaFuente protector, byte[]? actual, string? payload)
    {
        ArgumentNullException.ThrowIfNull(protector);
        if (actual is null)
            return null;
        var ok = EjecutorPorPersonas.Leer(payload)?.Personas.Where(p => p.Estado == EjecutorPorPersonas.EstadoOk).Select(p => Clave(p.Orden)).ToHashSet()
            ?? [];
        if (LeerMapa(protector, actual) is not { } mapa)
            return null;
        var podado = mapa.Where(e => ok.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        return podado.Count == mapa.Count ? actual : Proteger(protector, podado);
    }

    /// <summary>La respuesta de una fuente por vehículo como objeto JSON; false si hay cifrado pero no se puede leer.</summary>
    public static bool TryLeerVehiculo(IProtectorRespuestaFuente protector, byte[]? cifrada, out JsonElement? respuesta)
    {
        ArgumentNullException.ThrowIfNull(protector);
        respuesta = null;
        if (cifrada is null)
            return true;
        try
        {
            respuesta = Objeto(protector.Desproteger(cifrada));
            return respuesta is not null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Las respuestas de una fuente de persona por orden; false (y vacío) si hay cifrado pero no se puede leer.</summary>
    public static bool TryLeerPersonas(IProtectorRespuestaFuente protector, byte[]? cifrada, out IReadOnlyDictionary<int, JsonElement> respuestas)
    {
        ArgumentNullException.ThrowIfNull(protector);
        var leidas = new Dictionary<int, JsonElement>();
        respuestas = leidas;
        if (cifrada is null)
            return true;
        try
        {
            if (JsonSerializer.Deserialize<Dictionary<string, string>>(protector.Desproteger(cifrada)) is not { } mapa)
                return false;
            foreach (var (clave, texto) in mapa)
            {
                if (int.TryParse(clave, NumberStyles.None, CultureInfo.InvariantCulture, out var orden) && Objeto(texto) is { } objeto)
                    leidas[orden] = objeto;
            }

            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            leidas.Clear();
            return false;
        }
    }

    private static string Clave(int orden) => orden.ToString(CultureInfo.InvariantCulture);

    private static JsonElement? Objeto(string texto)
    {
        using var documento = JsonDocument.Parse(texto);
        return documento.RootElement.ValueKind == JsonValueKind.Object ? documento.RootElement.Clone() : null;
    }

    /// <summary>El objeto por orden de <paramref name="cifrada"/>; vacío si no hay, null si no se puede leer.</summary>
    private static Dictionary<string, string>? LeerMapa(IProtectorRespuestaFuente protector, byte[]? cifrada)
    {
        if (cifrada is null)
            return new(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(protector.Desproteger(cifrada)) is { } mapa
                ? new(mapa, StringComparer.Ordinal)
                : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private static byte[]? Proteger(IProtectorRespuestaFuente protector, Dictionary<string, string> mapa) =>
        mapa.Count == 0 ? null : protector.Proteger(JsonSerializer.Serialize(mapa));
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>Cabecera del lote que entra en la huella: id, nombre, modo, tipo de trámite y alcance (códigos de la base).</summary>
public sealed record CabeceraHuella(Guid Id, string Nombre, string Modo, string? TipoTramite, IReadOnlyList<string> Alcance)
{
    public static CabeceraHuella Desde(CabeceraDiagnostico cabecera)
    {
        ArgumentNullException.ThrowIfNull(cabecera);
        return new CabeceraHuella(
            cabecera.Id,
            cabecera.Nombre,
            CodigosDeEnum.Codigo(cabecera.Modo),
            cabecera.TipoTramiteCodigo,
            [.. cabecera.Alcance.Select(CodigosDeEnum.Codigo)]);
    }
}

/// <summary>
/// Una celda tal como entra en la huella: fuente, estado, motivo de N/A, tipo de error, fecha del dato, versión del tablero y el
/// payload normalizado (se huellea su SHA-256, no el texto). No lleva origen ni usuario manual (R-20): la invalidación la da
/// <see cref="VersionTablero"/>, que sube con todo cambio de celda (reproceso #13746 y llenado manual #13800 incluidos).
/// </summary>
public sealed record CeldaHuella(
    Guid VehiculoId,
    string Fuente,
    string Estado,
    string? MotivoNa,
    string? TipoError,
    DateTimeOffset? DatoObtenidoEn,
    long VersionTablero,
    string? Payload)
{
    public static CeldaHuella Desde(CeldaConsulta celda)
    {
        ArgumentNullException.ThrowIfNull(celda);
        return new CeldaHuella(
            celda.VehiculoId,
            CodigosDeEnum.Codigo(celda.Fuente),
            CodigosDeEnum.Codigo(celda.Estado),
            celda.MotivoNa is { } motivo ? CodigosDeEnum.Codigo(motivo) : null,
            celda.TipoError,
            celda.DatoObtenidoEn,
            celda.VersionTablero,
            celda.Payload);
    }
}

/// <summary>
/// Todo lo que define el contenido de un archivo: la versión de la plantilla del renderizador (súbela al cambiar el diseño), el
/// tipo, la cabecera, los nombres resueltos del encabezado (empresa, creador; clave → texto) y las celdas que salen en él.
/// </summary>
public sealed record EntradaHuella(
    int VersionPlantilla,
    TipoEvidencia Tipo,
    CabeceraHuella Cabecera,
    IReadOnlyDictionary<string, string?> Encabezado,
    IReadOnlyList<CeldaHuella> Celdas);

/// <summary>
/// Huella de las entradas de una evidencia (HU #13846, F07-C8): SHA-256 en hexadecimal minúsculo de un JSON canónico (claves en
/// orden ordinal, sin espacios) con la versión de la plantilla, el tipo, la cabecera (alcance ordenado), el encabezado y, por
/// celda (ordenadas por vehículo y fuente), fuente, estado, motivo de N/A, tipo de error, fecha del dato (UTC, al microsegundo,
/// la precisión de <c>timestamptz</c>), versión del tablero y el SHA-256 del payload normalizado (claves ordenadas a toda
/// profundidad). El orden en que llegan las celdas o las claves no cambia la huella; cualquier cambio de dato, sí.
/// </summary>
public static class HuellaEvidencia
{
    /// <summary>La huella (64 caracteres hex) de <paramref name="entrada"/>.</summary>
    public static string Calcular(EntradaHuella entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartObject("cabecera");
            json.WriteStartArray("alcance");
            foreach (var fuente in entrada.Cabecera.Alcance.Order(StringComparer.Ordinal))
                json.WriteStringValue(fuente);
            json.WriteEndArray();
            json.WriteString("id", entrada.Cabecera.Id);
            json.WriteString("modo", entrada.Cabecera.Modo);
            json.WriteString("nombre", entrada.Cabecera.Nombre);
            Texto(json, "tipo_tramite", entrada.Cabecera.TipoTramite);
            json.WriteEndObject();

            json.WriteStartArray("celdas");
            foreach (var celda in entrada.Celdas.OrderBy(c => c.VehiculoId).ThenBy(c => c.Fuente, StringComparer.Ordinal))
            {
                json.WriteStartObject();
                Texto(json, "dato_obtenido_en", celda.DatoObtenidoEn is { } dato ? Instante(dato) : null);
                json.WriteString("estado", celda.Estado);
                json.WriteString("fuente", celda.Fuente);
                Texto(json, "motivo_na", celda.MotivoNa);
                Texto(json, "payload_sha256", celda.Payload is null ? null : Sha256(PayloadNormalizado(celda.Payload)));
                Texto(json, "tipo_error", celda.TipoError);
                json.WriteString("vehiculo_id", celda.VehiculoId);
                json.WriteNumber("version_tablero", celda.VersionTablero);
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartObject("encabezado");
            foreach (var (clave, valor) in entrada.Encabezado.OrderBy(p => p.Key, StringComparer.Ordinal))
                Texto(json, clave, valor);
            json.WriteEndObject();

            json.WriteString("tipo", CodigosDeEnum.Codigo(entrada.Tipo));
            json.WriteNumber("version_plantilla", entrada.VersionPlantilla);
            json.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    /// <summary>
    /// Lee de la base (empresa del contexto) lo que entra en la huella de <paramref name="clave"/>: la cabecera y las celdas del
    /// vehículo (PDF) o de todo el lote (Excel, ZIP). El encabezado (nombres resueltos) lo pone quien llama. 404 si el lote, o el
    /// vehículo dentro de ese lote, no existe en la empresa.
    /// </summary>
    public static async Task<EntradaHuella> LeerEntradaAsync(
        DiagnosticoDb db, ClaveEvidencia clave, int versionPlantilla, IReadOnlyDictionary<string, string?> encabezado, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clave);
        ArgumentNullException.ThrowIfNull(encabezado);
        var cabecera = await db.Set<CabeceraDiagnostico>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == clave.DiagnosticoId, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El diagnóstico");
        var celdas = db.Set<CeldaConsulta>().AsNoTracking().Where(c => c.DiagnosticoId == clave.DiagnosticoId);
        if (clave.VehiculoId is { } vehiculo)
        {
            if (!await db.Set<VehiculoDiagnostico>().AnyAsync(v => v.Id == vehiculo && v.DiagnosticoId == clave.DiagnosticoId, ct).ConfigureAwait(false))
                throw ReglaException.NoEncontrado("El vehículo");
            celdas = celdas.Where(c => c.VehiculoId == vehiculo);
        }

        var filas = await celdas.ToListAsync(ct).ConfigureAwait(false);
        return new EntradaHuella(versionPlantilla, clave.Tipo, CabeceraHuella.Desde(cabecera), encabezado, [.. filas.Select(CeldaHuella.Desde)]);
    }

    private static void Texto(Utf8JsonWriter json, string clave, string? valor)
    {
        if (valor is null)
            json.WriteNull(clave);
        else
            json.WriteString(clave, valor);
    }

    // timestamptz guarda microsegundos: se trunca al microsegundo para que lo leído de la base huellee igual que lo escrito.
    private static string Instante(DateTimeOffset dato)
    {
        var utc = dato.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
    }

    private static string Sha256(string texto) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

    /// <summary>El payload con las claves ordenadas a toda profundidad y sin espacios; si no es JSON, el texto tal cual.</summary>
    private static string PayloadNormalizado(string payload)
    {
        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return payload;
        }

        using (documento)
        {
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer))
                Escribir(json, documento.RootElement);
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }

    private static void Escribir(Utf8JsonWriter json, JsonElement elemento)
    {
        switch (elemento.ValueKind)
        {
            case JsonValueKind.Object:
                json.WriteStartObject();
                foreach (var propiedad in elemento.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    json.WritePropertyName(propiedad.Name);
                    Escribir(json, propiedad.Value);
                }

                json.WriteEndObject();
                break;
            case JsonValueKind.Array:
                json.WriteStartArray();
                foreach (var item in elemento.EnumerateArray())
                    Escribir(json, item);
                json.WriteEndArray();
                break;
            default:
                elemento.WriteTo(json);
                break;
        }
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Flit.Tramites.Domain.RuntConfirmation;

/// <summary>
/// Vista NEUTRA de una respuesta cruda del RUNT, sea de Kyverum o de Verifik: lo único que el motor de
/// confirmación necesita leer. Se construye desde el JSON tal como llegó (no desde el
/// <c>ConsultationResult</c> del wizard, que no conserva <c>solicitudes[]</c>) para que un intento se
/// pueda re-evaluar desde <c>external_query_payloads</c> sin volver a pagar la consulta.
/// </summary>
public sealed record RuntVehicleSnapshot(
    RuntVehicleOutcome Outcome,
    string ProviderHint,
    string? Placa,
    string? Vin,
    string? EstadoAutomotor,
    string? Color,
    string? TipoCarroceria,
    string? TipoCombustible,
    string? TipoServicio,
    string? OrganismoTransito,
    string? Prendas,
    string? Gravamenes,
    DateOnly? FechaRegistro,
    string? MostrarSolicitudes,
    IReadOnlyList<RuntSolicitud> Solicitudes,
    IReadOnlyList<RuntGarantia> Garantias)
{
    /// <summary>El RUNT sí expone el historial de solicitudes para este vehículo.</summary>
    public bool ExponeSolicitudes =>
        string.Equals(MostrarSolicitudes?.Trim(), "SI", StringComparison.OrdinalIgnoreCase);

    public static RuntVehicleSnapshot NotFound(string providerHint) =>
        new(RuntVehicleOutcome.NotFound, providerHint, null, null, null, null, null, null, null, null, null, null, null, null, [], []);
}

public enum RuntVehicleOutcome
{
    /// <summary>El proveedor devolvió el vehículo.</summary>
    Found,

    /// <summary>El proveedor respondió que no existe (o que el documento no es del propietario).</summary>
    NotFound,

    /// <summary>El crudo no se pudo interpretar como respuesta de ninguno de los dos proveedores.</summary>
    Unreadable,
}

/// <summary>Una fila de <c>solicitudes[]</c>. <see cref="Fecha"/> es el DÍA: Verifik no trae hora.</summary>
public sealed record RuntSolicitud(
    string? NoSolicitud,
    DateOnly? Fecha,
    string? Estado,
    string? TramitesRealizados,
    string? Entidad)
{
    /// <summary>Segmentos de <c>tramitesRealizados</c> normalizados (mayúsculas, sin tildes, sin «TRÁMITE » ni coma final).</summary>
    public IReadOnlyList<string> Tramites { get; } = RuntText.SplitTramites(TramitesRealizados);

    public bool Contiene(string tramiteRunt) =>
        Tramites.Contains(RuntText.Normalize(tramiteRunt), StringComparer.Ordinal);

    public string EstadoNormalizado => RuntText.Normalize(Estado);
}

public sealed record RuntGarantia(string? Acreedor, string? DocumentoAcreedor, DateOnly? FechaInscripcion);

/// <summary>Estados de solicitud vistos en el RUNT. La solicitud más reciente del tipo esperado decide.</summary>
public static class RuntSolicitudEstados
{
    public const string Autorizada = "AUTORIZADA";
    public const string Aprobada = "APROBADA";
    public const string Registrada = "REGISTRADA";
    public const string Rechazada = "RECHAZADA";

    public static bool EsPositivo(string estadoNormalizado) =>
        estadoNormalizado is Autorizada or Aprobada;
}

/// <summary>Normalización de texto del RUNT: mayúsculas, sin tildes, espacios colapsados.</summary>
public static class RuntText
{
    private const string PrefijoTramite = "TRAMITE ";

    // Tabla propia de tildes en vez de string.Normalize(FormD): core-api corre con
    // InvariantGlobalization=true y ahí la descomposición Unicode no es fiable (deja «TRÁMITE» con
    // tilde y el rótulo nunca coincide). El RUNT solo escribe español: con esto basta.
    private static readonly Dictionary<char, char> SinTilde = new()
    {
        ['Á'] = 'A', ['É'] = 'E', ['Í'] = 'I', ['Ó'] = 'O', ['Ú'] = 'U', ['Ü'] = 'U', ['Ñ'] = 'N',
        ['À'] = 'A', ['È'] = 'E', ['Ì'] = 'I', ['Ò'] = 'O', ['Ù'] = 'U',
        ['Â'] = 'A', ['Ê'] = 'E', ['Î'] = 'I', ['Ô'] = 'O', ['Û'] = 'U',
        ['Ä'] = 'A', ['Ë'] = 'E', ['Ï'] = 'I', ['Ö'] = 'O',
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var raw in value)
        {
            var ch = char.ToUpperInvariant(raw);
            sb.Append(SinTilde.TryGetValue(ch, out var plain) ? plain : ch);
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// «TRÁMITE CAMBIO COLOR, TRÁMITE TRANSFORMACIÓN, » → ["CAMBIO COLOR", "TRANSFORMACION"]. Una
    /// solicitud puede traer varios trámites y siempre trae coma final: se compara por segmento,
    /// nunca por igualdad de la cadena entera.
    /// </summary>
    public static IReadOnlyList<string> SplitTramites(string? tramitesRealizados)
    {
        if (string.IsNullOrWhiteSpace(tramitesRealizados))
            return [];

        var result = new List<string>();
        foreach (var raw in tramitesRealizados.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = Normalize(raw);
            if (normalized.Length == 0)
                continue;
            if (normalized.StartsWith(PrefijoTramite, StringComparison.Ordinal))
                normalized = normalized[PrefijoTramite.Length..];
            result.Add(normalized);
        }

        return result;
    }

    /// <summary>
    /// Fecha del RUNT a DÍA. Kyverum manda ISO con hora y offset (<c>2026-09-08T15:49:49.000-05:00</c>):
    /// se toma la fecha tal como la escribió el proveedor (hora local del RUNT), no la UTC, para que
    /// coincida con Verifik, que manda solo <c>dd/MM/yyyy</c>. Comparar por día es lo que hace que el
    /// mismo trámite dé el mismo veredicto con los dos proveedores.
    /// </summary>
    public static DateOnly? ParseDay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var v = value.Trim();
        if (DateOnly.TryParseExact(v, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return day;

        if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return DateOnly.FromDateTime(dto.DateTime);

        if (DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            return day;

        return null;
    }
}

/// <summary>
/// Interpreta el JSON crudo de cualquiera de los dos proveedores. Detecta el proveedor por la FORMA
/// del documento (<c>data.vehiculo</c> = Kyverum, <c>data.informacionGeneral</c> = Verifik), no por
/// un parámetro: así una re-evaluación desde el crudo no depende de recordar quién respondió.
/// </summary>
public static class RuntVehicleSnapshotParser
{
    public const string KyverumHint = "kyverum";
    public const string VerifikHint = "verifik";

    /// <summary>Documento sintético que la corrida guarda cuando el proveedor responde «no encontrado» sin cuerpo útil (Verifik 404).</summary>
    public static string NotFoundPayload(string providerKey, int? statusCode, string? message) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["notFound"] = true,
            ["providerKey"] = providerKey,
            ["statusCode"] = statusCode,
            ["message"] = message,
        });

    public static RuntVehicleSnapshot Parse(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return Unreadable();

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Unreadable();

            // Cuerpo de un error HTTP del proveedor guardado como evidencia (Verifik 409/5xx): no es un
            // «no encontrado» aunque lleve ok:false; sin dato no hay veredicto.
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.True)
                return Unreadable();

            if (root.TryGetProperty("notFound", out var nf) && nf.ValueKind == JsonValueKind.True)
                return RuntVehicleSnapshot.NotFound(Str(root, "providerKey") ?? "desconocido");

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                return RuntVehicleSnapshot.NotFound(KyverumHint);

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return Unreadable();

            if (data.TryGetProperty("vehiculo", out var vehiculo) && vehiculo.ValueKind == JsonValueKind.Object)
                return ParseKyverum(data, vehiculo);

            if (data.TryGetProperty("informacionGeneral", out var info) && info.ValueKind == JsonValueKind.Object)
                return ParseVerifik(data, info);

            return Unreadable();
        }
        catch (JsonException)
        {
            return Unreadable();
        }
    }

    private static RuntVehicleSnapshot Unreadable() =>
        new(RuntVehicleOutcome.Unreadable, "desconocido", null, null, null, null, null, null, null, null, null, null, null, null, [], []);

    private static RuntVehicleSnapshot ParseKyverum(JsonElement data, JsonElement v) =>
        new(
            RuntVehicleOutcome.Found,
            KyverumHint,
            Placa: Str(v, "placa"),
            Vin: Str(v, "vin") ?? Str(v, "numChasis"),
            EstadoAutomotor: Str(v, "estadoAutomotor"),
            Color: Str(v, "color"),
            TipoCarroceria: Str(v, "tipoCarroceria"),
            TipoCombustible: Str(v, "tipoCombustible"),
            TipoServicio: Str(v, "tipoServicio"),
            OrganismoTransito: Str(v, "organismoTransito"),
            Prendas: Str(v, "prendas"),
            Gravamenes: Str(v, "gravamenes"),
            FechaRegistro: RuntText.ParseDay(Str(v, "fechaRegistro") ?? Str(v, "fechaMatricula")),
            MostrarSolicitudes: Str(v, "mostrarSolicitudes"),
            Solicitudes: ParseSolicitudes(data),
            Garantias: ParseGarantias(data, "garantias", "garantiasPrendas"));

    private static RuntVehicleSnapshot ParseVerifik(JsonElement data, JsonElement v) =>
        new(
            RuntVehicleOutcome.Found,
            VerifikHint,
            Placa: Str(v, "noPlaca") ?? Str(data, "plate"),
            Vin: Str(v, "noVin") ?? Str(data, "vin") ?? Str(v, "noChasis"),
            EstadoAutomotor: Str(v, "estadoDelVehiculo"),
            Color: Str(v, "color"),
            TipoCarroceria: Str(v, "tipoCarroceria"),
            TipoCombustible: Str(v, "tipoCombustible"),
            TipoServicio: Str(v, "tipoServicio"),
            OrganismoTransito: Str(v, "organismoTransito"),
            Prendas: Str(v, "prendas"),
            Gravamenes: Str(v, "tieneGravamenes"),
            FechaRegistro: RuntText.ParseDay(Str(v, "fechaMatricula")),
            MostrarSolicitudes: Str(v, "mostrarSolicitudes"),
            Solicitudes: ParseSolicitudes(data),
            Garantias: ParseGarantias(data, "garantiasFavorDe", "garantiasMobiliarias"));

    private static List<RuntSolicitud> ParseSolicitudes(JsonElement data)
    {
        if (!data.TryGetProperty("solicitudes", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<RuntSolicitud>();
        foreach (var s in arr.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object)
                continue;
            list.Add(new RuntSolicitud(
                Str(s, "noSolicitud"),
                RuntText.ParseDay(Str(s, "fechaSolicitud")),
                Str(s, "estado"),
                Str(s, "tramitesRealizados"),
                Str(s, "entidad")));
        }

        return list;
    }

    private static List<RuntGarantia> ParseGarantias(JsonElement data, params string[] arrays)
    {
        var list = new List<RuntGarantia>();
        foreach (var name in arrays)
        {
            if (!data.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var g in arr.EnumerateArray())
            {
                if (g.ValueKind != JsonValueKind.Object)
                    continue;
                list.Add(new RuntGarantia(
                    Str(g, "acreedor"),
                    Str(g, "numeroDocumentoAcreedor"),
                    RuntText.ParseDay(Str(g, "fechaInscripcion"))));
            }
        }

        return list;
    }

    private static string? Str(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var p))
            return null;

        return p.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(p.GetString()) ? null : p.GetString(),
            JsonValueKind.Number => p.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }
}

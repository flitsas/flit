using System.Globalization;
using System.Text.Json;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Credenciales;

namespace Flit.Impuestos.Api.Modulos.Resultados;

/// <summary>Una vigencia de <c>result.pendingPeriods</c>; todo monto puede ser null («Sin dato», nunca 0).</summary>
public sealed record PeriodoLeido(
    string Periodo,
    decimal? Avaluo,
    decimal? Impuesto,
    decimal? Sancion,
    decimal? Intereses,
    decimal? Descuentos,
    decimal? PagosPrevios,
    decimal? TotalAPagar,
    string OtrosCargosJson,
    DateOnly? Vencimiento,
    string? ReferenciaPago,
    string? FileId,
    bool NoLiquidable);

/// <summary>
/// Lo que el servicio usa de una consulta del proveedor (el <c>consulta</c> del aviso o la respuesta de
/// <c>GET /api/v1/consultas/{id}</c>, que tienen la misma forma, guía §6–§7): estado, <c>attempt</c>, error, montos sin PII y el
/// JSON completo de la consulta (<see cref="ConsultaJson"/>), que se guarda cifrado porque <c>validations[].sent</c> puede traer
/// datos del propietario. <see cref="ToString"/> no imprime el JSON.
/// </summary>
public sealed record ResultadoLeido(
    string ConsultaId,
    string Status,
    int Attempt,
    string? ErrorCodigo,
    string? ErrorMensaje,
    string? ResultadoStatus,
    string? DepartamentoCodigo,
    string? DepartamentoNombre,
    decimal? TotalAPagar,
    IReadOnlyList<PeriodoLeido> Periodos,
    IReadOnlyList<string> Warnings,
    bool TieneArchivos,
    string ConsultaJson)
{
    public const string Exitosa = "succeeded";
    public const string Parcial = "partial";
    public const string Fallida = "failed";

    /// <summary>La consulta terminó (<c>succeeded</c>, <c>partial</c> o <c>failed</c>); <c>queued</c> o <c>running</c> no.</summary>
    public bool EsFinal => Status is Exitosa or Parcial or Fallida;

    public override string ToString() =>
        $"ResultadoLeido {{ ConsultaId = {ConsultaId}, Status = {Status}, Attempt = {Attempt}, ErrorCodigo = {ErrorCodigo}, Periodos = {Periodos.Count}, [cuerpo REDACTADO] }}";
}

/// <summary>Un aviso del webhook ya leído: su <c>delivery_id</c> y su <c>event</c> del cuerpo, y la consulta.</summary>
public sealed record AvisoLeido(string? DeliveryId, string? Evento, ResultadoLeido Consulta);

/// <summary>Resultado de consultar una consulta por GET.</summary>
public enum ClaseLectura
{
    /// <summary>El proveedor respondió la consulta (final o en curso, ver <see cref="ResultadoLeido.EsFinal"/>).</summary>
    Leida,

    /// <summary>La compañía no tiene api_key guardada.</summary>
    SinCredencial,

    /// <summary>El proveedor respondió 401 a la api_key.</summary>
    CredencialRechazada,

    /// <summary>No salió, incierto, otro estado HTTP o cuerpo ilegible: se vuelve a intentar según el calendario.</summary>
    Fallo,
}

public sealed record LecturaConsulta(ClaseLectura Clase, ResultadoLeido? Resultado = null, int? Status = null);

/// <summary>
/// Lee el resultado de una consulta del proveedor (guía §6–§9, plan §3.2; los <c>file_errors</c> de un <c>partial</c> se suman a los warnings): del cuerpo crudo del aviso
/// (<see cref="LeerAviso"/>), del JSON de una consulta (<see cref="DesdeConsulta"/>) o pidiéndola por GET con la api_key de la
/// compañía (<see cref="ConsultarAsync"/>, respaldo y conciliación). Tolerante con la forma: un campo ausente o de otro tipo es
/// null, nunca 0 (F2-CF14). Un <c>warning</c> puede venir como texto o como objeto con <c>code</c>; el de una vigencia
/// (<c>period_not_liquidable</c>) la marca no liquidable. No registra nada del cuerpo.
/// </summary>
/// <example>
/// <code>
/// var aviso = LectorResultado.LeerAviso(cuerpoCrudo);              // null si no es un aviso legible
/// var lectura = await lector.ConsultarAsync(empresa, "8d1e0c55-…", ct);
/// </code>
/// </example>
public sealed class LectorResultado(ClienteApiLiquidacion api, AlmacenCredenciales almacen)
{
    public const string AvisoNoLiquidable = "period_not_liquidable";

    /// <summary>La consulta por GET con la api_key de la compañía, clasificada.</summary>
    public async Task<LecturaConsulta> ConsultarAsync(Guid empresa, string consultaId, CancellationToken ct)
    {
        var credenciales = await almacen.LeerAsync(empresa, ct).ConfigureAwait(false);
        if (credenciales is null)
            return new LecturaConsulta(ClaseLectura.SinCredencial);

        var r = await api.ObtenerConsultaAsync(credenciales.ApiKey, consultaId, ct).ConfigureAwait(false);
        if (r is { Clase: ClaseEnvio.Respuesta, Status: StatusCodes.Status401Unauthorized })
            return new LecturaConsulta(ClaseLectura.CredencialRechazada, Status: r.Status);
        if (!r.Exitoso)
            return new LecturaConsulta(ClaseLectura.Fallo, Status: r.Status);

        var leido = DesdeConsulta(r.Valor!.CuerpoCrudo);
        return leido is null ? new LecturaConsulta(ClaseLectura.Fallo, Status: r.Status) : new LecturaConsulta(ClaseLectura.Leida, leido, r.Status);
    }

    /// <summary>El aviso del webhook (<c>{delivery_id, event, consulta}</c>); null si no es JSON o no trae una consulta con id.</summary>
    public static AvisoLeido? LeerAviso(ReadOnlyMemory<byte> cuerpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("consulta", out var consulta))
                return null;
            var leido = Leer(consulta);
            return leido is null ? null : new AvisoLeido(Texto(doc.RootElement, "delivery_id"), Texto(doc.RootElement, "event"), leido);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Una consulta (forma de <c>GET /api/v1/consultas/{id}</c>); null si no es JSON o no trae id y estado.</summary>
    public static ResultadoLeido? DesdeConsulta(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Leer(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ResultadoLeido? Leer(JsonElement consulta)
    {
        if (consulta.ValueKind != JsonValueKind.Object || Texto(consulta, "id") is not { Length: > 0 } id || Texto(consulta, "status") is not { Length: > 0 } status)
            return null;

        var resultado = Objeto(consulta, "result");
        var error = resultado is { } r ? Objeto(r, "error") : null;
        var departamento = resultado is { } rd ? Objeto(rd, "department") : null;
        var totales = resultado is { } rt ? Objeto(rt, "totals") : null;
        var warnings = resultado is { } rw ? Codigos(rw, "warnings") : [];
        // partial: los montos se conservan y los errores de archivo quedan registrados junto a los warnings (guía §6).
        foreach (var codigo in Codigos(consulta, "file_errors"))
        {
            if (!warnings.Contains(codigo, StringComparer.Ordinal))
                warnings.Add(codigo);
        }
        var noLiquidables = resultado is { } rn ? PeriodosNoLiquidables(rn) : [];
        var periodos = new List<PeriodoLeido>();
        var hayArchivo = false;
        if (resultado is { } rp && rp.TryGetProperty("pendingPeriods", out var lista) && lista.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in lista.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object))
            {
                var periodo = TextoONumero(p, "period") ?? string.Empty;
                var fileId = Texto(p, "fileId");
                hayArchivo |= fileId is not null;
                periodos.Add(new PeriodoLeido(
                    periodo,
                    Monto(p, "appraisal"),
                    Monto(p, "tax"),
                    Monto(p, "penalty"),
                    Monto(p, "interest"),
                    Monto(p, "discounts"),
                    Monto(p, "previousPayments"),
                    Monto(p, "totalToPay"),
                    OtrosCargos(p),
                    Fecha(p, "dueDate"),
                    Texto(p, "paymentReference"),
                    fileId,
                    noLiquidables.Contains(periodo) || string.Equals(Texto(p, "warning"), AvisoNoLiquidable, StringComparison.Ordinal)));
            }
        }

        if (resultado is { } ra)
        {
            hayArchivo |= Texto(ra, "consolidatedFileId") is not null;
            hayArchivo |= Objeto(ra, "clearance") is { } paz && Texto(paz, "fileId") is not null;
        }

        return new ResultadoLeido(
            id,
            status,
            Entero(consulta, "attempt") ?? 1,
            Texto(consulta, "error_code") ?? (error is { } e1 ? Texto(e1, "code") : null),
            (error is { } e2 ? Texto(e2, "message") : null) ?? Texto(consulta, "error_message"),
            resultado is { } rs ? Texto(rs, "status") : null,
            departamento is { } d1 ? Texto(d1, "code") : null,
            departamento is { } d2 ? Texto(d2, "name") : null,
            totales is { } t ? Monto(t, "totalToPay") : null,
            periodos,
            warnings,
            hayArchivo,
            consulta.GetRawText());
    }

    /// <summary>Códigos de una lista de <c>warnings</c> o <c>file_errors</c>: texto, o el <c>code</c> (o <c>type</c>/<c>error_code</c>) de un objeto.</summary>
    private static List<string> Codigos(JsonElement padre, string nombre)
    {
        var codigos = new List<string>();
        if (!padre.TryGetProperty(nombre, out var lista) || lista.ValueKind != JsonValueKind.Array)
            return codigos;
        foreach (var w in lista.EnumerateArray())
        {
            var codigo = w.ValueKind switch
            {
                JsonValueKind.String => w.GetString(),
                JsonValueKind.Object => Texto(w, "code") ?? Texto(w, "type") ?? Texto(w, "error_code"),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(codigo) && !codigos.Contains(codigo, StringComparer.Ordinal))
                codigos.Add(codigo);
        }

        return codigos;
    }

    private static HashSet<string> PeriodosNoLiquidables(JsonElement resultado)
    {
        var periodos = new HashSet<string>(StringComparer.Ordinal);
        if (!resultado.TryGetProperty("warnings", out var lista) || lista.ValueKind != JsonValueKind.Array)
            return periodos;
        foreach (var w in lista.EnumerateArray().Where(w => w.ValueKind == JsonValueKind.Object))
        {
            if (string.Equals(Texto(w, "code") ?? Texto(w, "type"), AvisoNoLiquidable, StringComparison.Ordinal) && TextoONumero(w, "period") is { } p)
                periodos.Add(p);
        }

        return periodos;
    }

    /// <summary><c>otherCharges</c> con los nombres del contrato REST (<c>{descripcion, monto}</c>); el monto puede ser null.</summary>
    private static string OtrosCargos(JsonElement periodo)
    {
        if (!periodo.TryGetProperty("otherCharges", out var lista) || lista.ValueKind != JsonValueKind.Array)
            return "[]";
        var cargos = lista.EnumerateArray()
            .Where(c => c.ValueKind == JsonValueKind.Object)
            .Select(c => new { descripcion = Texto(c, "description") ?? string.Empty, monto = Monto(c, "amount") })
            .ToList();
        return JsonSerializer.Serialize(cargos);
    }

    private static JsonElement? Objeto(JsonElement padre, string nombre) =>
        padre.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static string? Texto(JsonElement padre, string nombre) =>
        padre.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? TextoONumero(JsonElement padre, string nombre) =>
        padre.TryGetProperty(nombre, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    private static decimal? Monto(JsonElement padre, string nombre) =>
        padre.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    private static int? Entero(JsonElement padre, string nombre) =>
        padre.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static DateOnly? Fecha(JsonElement padre, string nombre) =>
        Texto(padre, nombre) is { } s && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
}

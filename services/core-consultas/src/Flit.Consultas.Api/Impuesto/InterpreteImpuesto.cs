using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Lee el cuerpo del GET /consultas/{id} en estado terminal (diseño §2.5). Lo primero que aplica gana.
/// Nunca copia <c>sent</c>, <c>portal</c> ni <c>message</c>.
/// </summary>
internal static class InterpreteImpuesto
{
    private static readonly HashSet<string> CamposDetail = new(StringComparer.Ordinal)
    {
        "secretaryDepartmentCode", "secretaryDepartmentDescription", "secretaryName", "secretaryCity",
        "vehicleModel", "vehicleServiceType", "vehicleFuelType", "vehicleType", "vehicleBrand",
        "OwnerName", "OwnerCellPhone", "OwnerPhone", "OwnerEmail",
        "OwnerAddressDepartment", "OwnerAddressCity", "OwnerAddressComplete",
        "OwnerAddressTypeRoad", "OwnerAddressNumber1", "OwnerAddressLetter1", "OwnerAddressBis1",
        "OwnerAddressSuffixletter1", "OwnerAddressCardinality1", "OwnerAddressNumber2", "OwnerAddressLetter2",
        "OwnerAddressBis2", "OwnerAddressSuffixletter2", "OwnerAddressPlate", "OwnerAddressCardinality2",
    };

    private static readonly Regex CodigoAviso = new("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex IdArchivo = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReferenciaPago = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static VeredictoImpuesto Interpretar(string json)
    {
        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Error("RESPUESTA_INVALIDA");
        }

        using (documento)
        {
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
                return Error("RESPUESTA_INVALIDA");

            var advertencias = LeerAdvertencias(raiz);
            var status = Texto(raiz, "status");
            var result = Objeto(raiz, "result");
            var resultStatus = result.ValueKind == JsonValueKind.Object ? Texto(result, "status") : "";
            var errorCode = Texto(raiz, "error_code");

            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resultStatus, "error", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(errorCode, "invalid_params", StringComparison.OrdinalIgnoreCase))
                    return Error("SOLICITUD_INVALIDA", advertencias);
                return Error("NO_CONFIRMADO", advertencias);
            }

            if (HayMismatchBloqueante(advertencias))
                return Error("NO_CONFIRMADO", advertencias);

            var (vigencias, parcialPeriodos) = Vigencias(result);
            var deudaLeida = string.Equals(resultStatus, "con_deuda", StringComparison.OrdinalIgnoreCase)
                || vigencias.Any(v => v.Valor > 0);

            if (string.Equals(status, "partial", StringComparison.OrdinalIgnoreCase))
            {
                if (deudaLeida)
                    return Confirmado(result, true, MontoTotal(result), vigencias, datoParcial: true, advertencias);
                return new VeredictoImpuesto(false, "NO_CONFIRMADO", false, null, false, true, vigencias,
                    null, null, null, null, null, null, null, null, null, advertencias);
            }

            if (string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(resultStatus, "con_deuda", StringComparison.OrdinalIgnoreCase))
                    return Confirmado(result, true, MontoTotal(result), vigencias, parcialPeriodos, advertencias);

                if (string.Equals(resultStatus, "paz_y_salvo", StringComparison.OrdinalIgnoreCase))
                {
                    if (vigencias.Any(v => v.Valor > 0))
                        return Error("NO_CONFIRMADO", advertencias);
                    return Confirmado(result, false, null, vigencias, parcialPeriodos, advertencias);
                }
            }

            return Error("NO_CONFIRMADO", advertencias);
        }
    }

    private static VeredictoImpuesto Confirmado(
        JsonElement result, bool deuda, long? monto, List<VigenciaLeida> vigencias, bool datoParcial,
        IReadOnlyList<AdvertenciaLeida> advertencias)
    {
        var totals = Objeto(result, "totals");
        var ultimo = Objeto(result, "lastPayment");
        var avaluoComercial = vigencias.Count == 0
            ? null
            : vigencias.OrderByDescending(v => v.Anio).First().Avaluo;
        return new VeredictoImpuesto(
            true,
            null,
            deuda,
            monto,
            deuda && monto is not null,
            datoParcial,
            vigencias,
            Monto(totals, "interest"),
            Monto(totals, "penalty"),
            avaluoComercial,
            IdSeguro(Texto(result, "consolidatedFileId")),
            null,
            null,
            Anio(ultimo, "period"),
            Monto(ultimo, "amount"),
            LectorFechaImpuesto.Leer(Texto(ultimo, "date")),
            advertencias);
    }

    private static VeredictoImpuesto Error(string codigo, IReadOnlyList<AdvertenciaLeida>? advertencias = null) =>
        new(false, codigo, false, null, false, false, [], null, null, null, null, null, null, null, null, null,
            advertencias ?? []);

    private static bool HayMismatchBloqueante(IReadOnlyList<AdvertenciaLeida> advertencias) =>
        advertencias.Any(a => a.Tipo == TipoAdvertenciaLeida.Validacion
            && (a.Campo is "OwnerName" or "vehicleModel"));

    private static List<AdvertenciaLeida> LeerAdvertencias(JsonElement raiz)
    {
        var lista = new List<AdvertenciaLeida>();
        var result = Objeto(raiz, "result");
        if (result.ValueKind != JsonValueKind.Object)
            return lista;

        if (result.TryGetProperty("validations", out var validations) && validations.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in validations.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (!string.Equals(Texto(item, "result"), "mismatch", StringComparison.OrdinalIgnoreCase))
                    continue;
                var campo = Texto(item, "field");
                lista.Add(new AdvertenciaLeida(
                    TipoAdvertenciaLeida.Validacion,
                    CamposDetail.Contains(campo) ? campo : "otro",
                    "",
                    null));
            }
        }

        if (result.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in warnings.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var codigo = Texto(item, "code");
                lista.Add(new AdvertenciaLeida(
                    TipoAdvertenciaLeida.Aviso,
                    "",
                    CodigoAviso.IsMatch(codigo) ? codigo : "otro",
                    Anio(item, "period")));
            }
        }

        return lista;
    }

    private static (List<VigenciaLeida> Lista, bool Parcial) Vigencias(JsonElement result)
    {
        var lista = new List<VigenciaLeida>();
        var parcial = false;
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("pendingPeriods", out var arreglo)
            || arreglo.ValueKind != JsonValueKind.Array)
            return (lista, false);

        foreach (var item in arreglo.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                parcial = true;
                continue;
            }

            var anio = Anio(item, "period");
            var valor = Monto(item, "totalToPay") ?? 0;
            if (anio is null)
            {
                if (valor > 0)
                    parcial = true;
                continue;
            }

            lista.Add(new VigenciaLeida(
                anio.Value,
                valor,
                false,
                IdSeguro(Texto(item, "fileId")),
                Monto(item, "appraisal"),
                RefSegura(Texto(item, "paymentReference")),
                LectorFechaImpuesto.Leer(Texto(item, "dueDate"))));
        }

        return (lista, parcial);
    }

    private static long? MontoTotal(JsonElement result)
    {
        var totals = Objeto(result, "totals");
        var total = Monto(totals, "totalToPay");
        return total is > 0 ? total : null;
    }

    private static long? Monto(JsonElement elemento, string campo)
    {
        if (elemento.ValueKind != JsonValueKind.Object || !elemento.TryGetProperty(campo, out var nodo)
            || nodo.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return nodo.ValueKind switch
        {
            JsonValueKind.Number => Redondear(nodo.GetDecimal()),
            JsonValueKind.String => LectorMontoImpuesto.Leer(nodo.GetString()) ?? RedondearTexto(nodo.GetString()),
            _ => null,
        };
    }

    private static long? Redondear(decimal valor) =>
        valor < 0 ? null : (long)Math.Round(valor, MidpointRounding.AwayFromZero);

    private static long? RedondearTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        return decimal.TryParse(texto.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? Redondear(d)
            : null;
    }

    private static int? Anio(JsonElement elemento, string campo)
    {
        if (elemento.ValueKind != JsonValueKind.Object || !elemento.TryGetProperty(campo, out var nodo))
            return null;
        return nodo.ValueKind switch
        {
            JsonValueKind.Number when nodo.TryGetInt32(out var n) => n is >= 1900 and <= 2100 ? n : null,
            JsonValueKind.String => LectorFechaImpuesto.Anio(nodo.GetString()),
            _ => null,
        };
    }

    private static string? IdSeguro(string texto)
    {
        var limpio = texto.Trim();
        return limpio.Length > 0 && IdArchivo.IsMatch(limpio) ? limpio : null;
    }

    private static string? RefSegura(string texto)
    {
        var limpio = texto.Trim();
        return limpio.Length > 0 && ReferenciaPago.IsMatch(limpio) ? limpio : null;
    }

    private static JsonElement Objeto(JsonElement raiz, string campo) =>
        raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty(campo, out var valor)
        && valor.ValueKind == JsonValueKind.Object
            ? valor
            : default;

    private static string Texto(JsonElement elemento, string campo)
    {
        if (elemento.ValueKind != JsonValueKind.Object || !elemento.TryGetProperty(campo, out var valor))
            return "";
        return valor.ValueKind switch
        {
            JsonValueKind.String => valor.GetString() ?? "",
            JsonValueKind.Number => valor.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }
}

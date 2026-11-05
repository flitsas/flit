using System.Text.Json;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Lee el JSON crudo del servicio de impuesto. Lo primero que aplica gana, y <c>pendingDebtTaxes</c> va al final.
/// Un <c>typeError</c> en la raíz no cuenta: solo el de <c>observationError</c>, y solo si es un número.
/// </summary>
internal static class InterpreteImpuesto
{
    private static readonly VeredictoImpuesto SinConfirmar = Error("NO_CONFIRMADO");

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
            if (!raiz.EnumerateObject().MoveNext())
                return SinConfirmar;

            var typeError = TypeErrorExplicito(raiz);
            if (typeError is null)
                return SinConfirmar;

            var observacion = Objeto(raiz, "observationError");
            var frase = ClasificadorRespuestaImpuesto.Clasificar(
                typeError.Value,
                Texto(observacion, "descriptionError"),
                Texto(observacion, "descriptionErrorDevelopment"),
                Texto(raiz, "pendingValidity"));
            if (typeError != 0 || frase != "NO_CONFIRMADO")
                return ErrorConDatos(raiz, frase);

            if (Deuda(raiz) is not { } deuda)
                return SinConfirmar;
            if (!deuda)
            {
                if (HayMontoDeDeuda(raiz) || IndicaVigencias(Texto(raiz, "pendingValidity")))
                    return SinConfirmar;
                return Confirmado(raiz, false, null, []);
            }

            var (vigencias, ilegible) = Vigencias(raiz);
            var total = MontoDeuda(LectorMontoImpuesto.Leer(Texto(raiz, "pendingTotalToPay")));
            if (ilegible)
                return Confirmado(raiz, true, total, []);

            if (vigencias.Count == 0)
                return Confirmado(raiz, true, total, vigencias);

            long suma = 0;
            foreach (var vigencia in vigencias)
                suma += vigencia.Valor;
            var sumaDeuda = MontoDeuda(suma);
            long? monto = total is null || sumaDeuda is null || total == sumaDeuda ? sumaDeuda ?? total : null;
            if (total is not null && sumaDeuda is not null && total != sumaDeuda)
                monto = null;
            return Confirmado(raiz, true, monto, vigencias);
        }
    }

    private static VeredictoImpuesto ErrorConDatos(JsonElement raiz, string codigo)
    {
        var deuda = Deuda(raiz) == true;
        var monto = MontoDeuda(LectorMontoImpuesto.Leer(Texto(raiz, "pendingTotalToPay")));
        if (monto is null)
        {
            var (vigencias, ilegible) = Vigencias(raiz);
            if (!ilegible)
            {
                long suma = 0;
                foreach (var vigencia in vigencias)
                    suma += vigencia.Valor;
                monto = MontoDeuda(suma);
            }
        }

        var parcial = deuda && monto is not null;
        return new VeredictoImpuesto(false, codigo, deuda && parcial, monto, false, parcial, [], null, null, null, null, null, null, null, null, null);
    }

    private static VeredictoImpuesto Confirmado(JsonElement raiz, bool deuda, long? monto, IReadOnlyList<VigenciaLeida> vigencias)
    {
        var ultimoAnio = LectorFechaImpuesto.Anio(Texto(raiz, "lastPaymentMadeYear"));
        var ultimoValor = LectorMontoImpuesto.Leer(Texto(raiz, "lastPaymentMadeAmountPaid"));
        var ultimoFecha = LectorFechaImpuesto.Leer(Texto(raiz, "lastPaymentMadeDate"));
        return new VeredictoImpuesto(
            true,
            null,
            deuda,
            monto,
            deuda && monto is not null,
            false,
            vigencias,
            LectorMontoImpuesto.Leer(Texto(raiz, "pendingInterestLate")),
            LectorMontoImpuesto.Leer(Texto(raiz, "pendingPenalties")),
            LectorMontoImpuesto.Leer(Texto(raiz, "currentVehicleCommercialAppraisal")),
            Presente(Texto(raiz, "idAttachedTaxSettlementConsolidated")),
            Presente(Texto(raiz, "currentCertificationPeriod")),
            LectorFechaImpuesto.Leer(Texto(raiz, "currentConsultationDate")),
            ultimoAnio,
            ultimoValor,
            ultimoFecha);
    }

    private static VeredictoImpuesto Error(string codigo) =>
        new(false, codigo, false, null, false, false, [], null, null, null, null, null, null, null, null, null);

    private static int? TypeErrorExplicito(JsonElement raiz)
    {
        var observacion = Objeto(raiz, "observationError");
        if (observacion.ValueKind != JsonValueKind.Object || !observacion.TryGetProperty("typeError", out var valor))
            return null;
        return valor.ValueKind == JsonValueKind.Number && valor.TryGetInt32(out var numero) ? numero : null;
    }

    private static bool? Deuda(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("pendingDebtTaxes", out var valor))
            return null;
        return valor.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static bool HayMontoDeDeuda(JsonElement raiz)
    {
        foreach (var campo in new[] { "pendingTotalToPay", "pendingVehicleTaxValue", "pendingPenalties", "pendingInterestLate" })
        {
            if (MontoDeuda(LectorMontoImpuesto.Leer(Texto(raiz, campo))) is not null)
                return true;
        }

        var (vigencias, ilegible) = Vigencias(raiz);
        if (ilegible)
            return false;
        foreach (var vigencia in vigencias)
        {
            if (vigencia.Valor > 0)
                return true;
        }

        return false;
    }

    private static bool IndicaVigencias(string validez)
    {
        var texto = TextoImpuesto.Normalizar(validez);
        if (texto.Length == 0)
            return false;
        if (texto.Contains("vigencia", StringComparison.Ordinal) || texto.Contains("recibo", StringComparison.Ordinal))
            return true;
        for (var i = 0; i <= texto.Length - 4; i++)
        {
            if (i > 0 && char.IsDigit(texto[i - 1]))
                continue;
            if (i + 4 < texto.Length && char.IsDigit(texto[i + 4]))
                continue;
            if (texto[i] is '1' or '2' && char.IsDigit(texto[i + 1]) && char.IsDigit(texto[i + 2]) && char.IsDigit(texto[i + 3]))
                return true;
        }

        return false;
    }

    private static (List<VigenciaLeida> Lista, bool Ilegible) Vigencias(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("pendingDetailTax", out var arreglo))
            return ([], false);
        if (arreglo.ValueKind != JsonValueKind.Array)
            return ([], true);
        var lista = new List<VigenciaLeida>();
        foreach (var item in arreglo.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return ([], true);
            var anio = LectorFechaImpuesto.Anio(Primero(item, "validaty", "validity"));
            if (anio is null)
                return ([], true);
            if (!TryValor(item, "total", out var valor))
                return ([], true);
            if (!TryOpcional(item, "vehicleAppraisal", out var avaluo))
                return ([], true);
            lista.Add(new VigenciaLeida(anio.Value, valor, false, Presente(Texto(item, "idAttachedTaxSettlement")), avaluo));
        }

        return (lista, false);
    }

    private static bool TryValor(JsonElement item, string campo, out long valor)
    {
        valor = 0;
        if (!item.TryGetProperty(campo, out var nodo) || nodo.ValueKind == JsonValueKind.Null)
            return true;
        if (nodo.ValueKind != JsonValueKind.String)
            return false;
        var leido = LectorMontoImpuesto.Leer(nodo.GetString());
        if (leido is null && !string.IsNullOrWhiteSpace(nodo.GetString()))
            return false;
        valor = leido ?? 0;
        return true;
    }

    private static bool TryOpcional(JsonElement item, string campo, out long? valor)
    {
        valor = null;
        if (!item.TryGetProperty(campo, out var nodo) || nodo.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;
        if (nodo.ValueKind != JsonValueKind.String)
            return false;
        var crudo = nodo.GetString();
        if (string.IsNullOrWhiteSpace(crudo))
            return true;
        valor = LectorMontoImpuesto.Leer(crudo);
        return valor is not null;
    }

    private static long? MontoDeuda(long? valor) => valor is > 0 ? valor : null;

    private static JsonElement Objeto(JsonElement raiz, string campo) =>
        raiz.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.Object ? valor : default;

    private static string Texto(JsonElement elemento, string campo)
    {
        if (elemento.ValueKind != JsonValueKind.Object || !elemento.TryGetProperty(campo, out var valor) || valor.ValueKind != JsonValueKind.String)
            return "";
        return valor.GetString() ?? "";
    }

    private static string Primero(JsonElement elemento, string preferido, string alterno)
    {
        var uno = Texto(elemento, preferido);
        return uno.Length > 0 ? uno : Texto(elemento, alterno);
    }

    private static string? Presente(string texto)
    {
        var limpio = texto.Trim();
        return limpio.Length == 0 ? null : limpio;
    }
}

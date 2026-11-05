using System.Text.Json;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Lo que el SIMIT de Verifik trae fuera de <c>comparendos</c>: totales, multas y acuerdos. No copia al infractor
/// (nombre y documento) ni la firma del integrador.
/// </summary>
internal static class SimitVisible
{
    public static ResultadoSimitDetalle Completar(ResultadoSimitDetalle actual, JsonElement crudo)
    {
        var data = crudo.ValueKind == JsonValueKind.Object && crudo.TryGetProperty("data", out var anidado) && anidado.ValueKind == JsonValueKind.Object
            ? anidado
            : crudo;
        var multas = Multas(data);
        var acuerdos = Acuerdos(data);
        return actual with
        {
            TotalGeneral = Texto(data, "totalGeneral") ?? actual.TotalGeneral,
            TotalPorPagar = Texto(data, "totalMultasPagar") ?? actual.TotalPorPagar,
            PazYSalvo = Bandera(data, "pazSalvo") ?? actual.PazYSalvo,
            Suspendida = Bandera(data, "suspendida") ?? actual.Suspendida,
            Cancelada = Bandera(data, "cancelada") ?? actual.Cancelada,
            Multas = multas.Count > 0 ? multas : actual.Multas,
            Acuerdos = acuerdos.Count > 0 ? acuerdos : actual.Acuerdos,
        };
    }

    private static List<MultaSimitDetalle> Multas(JsonElement data)
    {
        var filas = new List<MultaSimitDetalle>();
        if (Arreglo(data, "multas") is not { } lista)
            return filas;
        foreach (var item in lista.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var codigos = Arreglo(item, "infracciones") is { } infracciones
                ? string.Join(", ", infracciones.EnumerateArray().Select(i => Texto(i, "codigoInfraccion")).Where(c => c is not null))
                : null;
            var fila = new MultaSimitDetalle(
                Texto(item, "numeroComparendo", "numero"), Texto(item, "fechaComparendo", "fecha"), Texto(item, "valor"),
                Texto(item, "valorPagar"), Texto(item, "estadoCartera", "estado"), Texto(item, "organismoTransito", "organismo"),
                string.IsNullOrWhiteSpace(codigos) ? null : codigos);
            if (fila.Numero is not null || fila.Valor is not null || fila.EstadoCartera is not null)
                filas.Add(fila);
        }

        return filas;
    }

    private static List<AcuerdoSimitDetalle> Acuerdos(JsonElement data)
    {
        var filas = new List<AcuerdoSimitDetalle>();
        if (Arreglo(data, "acuerdosPago") is not { } lista)
            return filas;
        foreach (var item in lista.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var fila = new AcuerdoSimitDetalle(
                Texto(item, "estado"), Texto(item, "cantCuotasAbonadas"), Texto(item, "cantCuotasPendientes"),
                Texto(item, "pendiente"), Texto(item, "fechaResolucion", "fecha"), Texto(item, "organismoTransito", "organismo"));
            if (fila.Estado is not null || fila.Pendiente is not null)
                filas.Add(fila);
        }

        return filas;
    }

    private static JsonElement? Arreglo(JsonElement objeto, string propiedad) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.Array ? valor : null;

    private static bool? Bandera(JsonElement objeto, string propiedad)
    {
        if (objeto.ValueKind != JsonValueKind.Object || !objeto.TryGetProperty(propiedad, out var valor))
            return null;
        return valor.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string? Texto(JsonElement objeto, params string[] propiedades)
    {
        if (objeto.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var propiedad in propiedades)
        {
            if (!objeto.TryGetProperty(propiedad, out var valor))
                continue;
            var texto = valor.ValueKind switch
            {
                JsonValueKind.String => valor.GetString()?.Trim(),
                JsonValueKind.Number => valor.GetRawText(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(texto))
                return texto;
        }

        return null;
    }
}

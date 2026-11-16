using System.Text.Json;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Devuelve el JSON crudo del fixture según el penúltimo carácter de la placa. El último lo usa el RUNT.
/// Pasa por el mismo intérprete que el transporte HTTP.
/// </summary>
internal sealed class TransporteImpuestoSimulado : ITransporteImpuesto
{
    public bool Listo => true;

    public string NombreProveedor => "impuesto_simulado";

    public Task<ResultadoTransporteImpuesto> EnviarAsync(string cuerpo, CancellationToken ct)
    {
        using var documento = JsonDocument.Parse(cuerpo);
        var placa = documento.RootElement.TryGetProperty("plateNumber", out var valor) ? valor.GetString() ?? "" : "";
        var marca = placa.Length >= 2 ? placa[^2] : '2';
        var nombre = marca switch
        {
            '0' => "deuda-completa",
            '1' => "deuda-sin-monto",
            '2' => "sin-deuda-con-datos",
            '3' => "sin-deuda-en-blanco",
            '4' => "bandera-falsa-con-timeout",
            '5' => "error-con-monto",
            '6' => "documento-no-coincide",
            '7' => "vehiculo-no-encontrado",
            '8' => "vacia",
            '9' => "corta-sin-typeerror",
            _ => "sin-deuda-con-datos",
        };
        var json = Simulacion.FixturesProveedor.Todos()[$"proveedor-impuesto-{nombre}.json"];
        return Task.FromResult(new ResultadoTransporteImpuesto(json, null));
    }
}

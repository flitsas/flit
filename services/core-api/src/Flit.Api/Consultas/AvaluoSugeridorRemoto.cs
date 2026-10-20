using System.Globalization;
using Flit.Consultas.Grpc.Mapping;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.Avaluos;
using Grpc.Core;

namespace Flit.Api.Consultas;

/// <summary>
/// HU #13348 (Epic #13316; ADR-0065): el valor comercial sugerido sale de <c>ConsultasService.ConsultarAvaluos</c>; los
/// proveedores de avalúo, su política por empresa y los valores del modo mock viven en core-consultas. Sin respaldo en
/// proceso: si Consultas no responde, la sugerencia queda vacía (la tarjeta muestra «sin datos» y el FUR deja las filas
/// en blanco, como cuando ninguna fuente respondía).
/// </summary>
internal sealed class AvaluoSugeridorRemoto(ConsultasService.ConsultasServiceClient client, ILogger<AvaluoSugeridorRemoto> logger) : IAvaluoSugeridor
{
    private static readonly SuggestedCommercialValue Vacio = new(null, null, []);

    public async Task<SuggestedCommercialValue> SugerirAsync(
        Guid instanceId, Guid tenantId, IReadOnlyDictionary<string, string?> fieldValues, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fieldValues);
        string? Valor(string clave) => fieldValues.TryGetValue(clave, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        int Numero(string clave) => int.TryParse(Valor(clave), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

        var vin = Valor("vin");
        var placa = Valor("plate");
        if (vin is null && placa is null)
            return Vacio; // Sin identificador ninguna fuente puede responder: no vale la llamada.

        var request = new ConsultarAvaluosRequest
        {
            Opciones = new OpcionesConsulta { Referencia = instanceId == Guid.Empty ? string.Empty : instanceId.ToString() },
            Vin = vin is null ? null : new Flit.Platform.Grpc.V1.Vin { Valor = vin },
            Placa = placa is null ? null : new Flit.Platform.Grpc.V1.Placa { Valor = placa },
            Modelo = Numero("vehicle_year"),
            Cilindraje = Numero("vehicle_engine_displacement"),
            Combustible = Valor("vehicle_fuel") ?? string.Empty,
            Pasajeros = Numero("vehicle_passengers"),
        };

        try
        {
            // La empresa del trámite (puede no ser la del usuario: un SuperAdmin opera por otra) va explícita.
            var respuesta = await client.ConsultarAvaluosAsync(
                request,
                new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, tenantId.ToString() } },
                cancellationToken: ct).ConfigureAwait(false);
            return ResultadoConsultaMapper.FromProto(respuesta);
        }
        catch (Exception ex) when (ex is RpcException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            ConsultasRemotasLog.AvaluosNoDisponibles(logger, tenantId, ex is RpcException rpc ? rpc.StatusCode.ToString() : "sin token", ex);
            return Vacio;
        }
    }
}

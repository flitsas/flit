using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>Respuesta de una llamada a core-consultas: la respuesta gRPC o el error ya traducido, nunca los dos.</summary>
public sealed record RespuestaConsultas<T>(T? Respuesta, ErrorConsulta? Error)
    where T : class
{
    public bool Exitosa => Respuesta is not null;

    /// <summary>Código gRPC del fallo (HU #13861: la capacidad lo registra); null si la llamada fue exitosa.</summary>
    public StatusCode? Codigo { get; init; }
}

/// <summary>
/// Puerta única de Diagnóstico hacia <c>ConsultasService</c> de core-consultas (HU #13791, F05-C1): el producto no guarda
/// credenciales de proveedores ni elige proveedor. Token de servicio de svc-diagnostico con scope <see cref="Scope"/>, deadline
/// <see cref="ClaveDeadline"/> (por defecto <see cref="DeadlinePorDefectoSegundos"/> s), y la empresa de la celda en la metadata
/// <c>x-flit-tenant-id</c> (el procesador corre sin petición HTTP, así que la empresa va explícita). Un error gRPC se devuelve
/// traducido (<see cref="TraductorErroresConsultas"/>); la cancelación de quien llama se propaga. Solo se registra en
/// <see cref="ModoAdaptador.Real"/>: con la simulación o sin dirección no existe y ninguna llamada sale a core-consultas.
/// Los logs llevan el método y el código; nunca documentos ni el detalle del proveedor.
/// </summary>
/// <example>
/// <code>
/// var respuesta = await cliente.LlamarAsync(celda.TenantId,
///     (c, opciones) => c.ConsultarMultasAsync(new ConsultarMultasRequest { Placa = placa }, opciones), ct);
/// var error = respuesta.Error ?? TraductorErroresConsultas.Traducir(respuesta.Respuesta!.Resultado);
/// </code>
/// </example>
public sealed partial class ClienteConsultas(ConsultasService.ConsultasServiceClient cliente, ILogger<ClienteConsultas> log)
{
    public const string Scope = "platform.consultas";
    public const string ClaveDireccion = "Diagnostico:Consultas:Address";
    public const string ClaveDeadline = "Diagnostico:Consultas:DeadlineSegundos";
    public const int DeadlinePorDefectoSegundos = 90;

    public async Task<RespuestaConsultas<T>> LlamarAsync<T>(
        Guid empresa, Func<ConsultasService.ConsultasServiceClient, CallOptions, AsyncUnaryCall<T>> llamada, CancellationToken ct)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(llamada);
        if (empresa == Guid.Empty)
            throw new ArgumentException("La consulta exige la empresa de la celda.", nameof(empresa));

        var metadata = new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, empresa.ToString() } };
        return await LlamarConAsync(new CallOptions(metadata, cancellationToken: ct), llamada, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Llamada a un rpc de capacidad global, igual para todas las empresas (HU #13861: <c>ListarDepartamentosImpuesto</c>, Q4=a):
    /// sin <c>x-flit-tenant-id</c>, con el mismo token de servicio, deadline y traducción de errores que <see cref="LlamarAsync"/>.
    /// </summary>
    public Task<RespuestaConsultas<T>> LlamarGlobalAsync<T>(
        Func<ConsultasService.ConsultasServiceClient, CallOptions, AsyncUnaryCall<T>> llamada, CancellationToken ct)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(llamada);
        return LlamarConAsync(new CallOptions(cancellationToken: ct), llamada, ct);
    }

    private async Task<RespuestaConsultas<T>> LlamarConAsync<T>(
        CallOptions opciones, Func<ConsultasService.ConsultasServiceClient, CallOptions, AsyncUnaryCall<T>> llamada, CancellationToken ct)
        where T : class
    {
        try
        {
            using var grpc = llamada(cliente, opciones);
            return new RespuestaConsultas<T>(await grpc.ResponseAsync.ConfigureAwait(false), null);
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            var error = TraductorErroresConsultas.Traducir(ex);
            LlamadaFallida(log, typeof(T).Name, ex.StatusCode, error.TipoError);
            return new RespuestaConsultas<T>(null, error) { Codigo = ex.StatusCode };
        }
    }

    [LoggerMessage(EventId = 137910, Level = LogLevel.Warning, Message = "core-consultas respondió {Codigo} a {Respuesta}; queda como {TipoError}")]
    private static partial void LlamadaFallida(ILogger logger, string respuesta, StatusCode codigo, string tipoError);
}

/// <summary>Registro del cliente gRPC de core-consultas (HU #13791); lo llama <c>Modulos.AddFuentesConsultas</c> en modo real.</summary>
internal static class RegistroClienteConsultas
{
    public static IServiceCollection AddClienteConsultas(this IServiceCollection services, IConfiguration configuration, SeleccionAdaptadores seleccion)
    {
        ArgumentNullException.ThrowIfNull(seleccion);
        if (seleccion.Modo != ModoAdaptador.Real || seleccion.Direccion is null)
            throw new InvalidOperationException("El cliente de core-consultas solo se registra con adaptadores reales.");

        services.AddFlitGrpcClient<ConsultasService.ConsultasServiceClient>(
            configuration, seleccion.Direccion, ClienteConsultas.Scope, o => o.Deadline = seleccion.Deadline);
        services.TryAddScoped<ClienteConsultas>();
        return services;
    }
}

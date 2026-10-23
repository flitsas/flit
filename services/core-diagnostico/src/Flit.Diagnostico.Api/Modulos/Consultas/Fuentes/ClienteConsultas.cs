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
/// Los logs llevan el método y el código; nunca documentos ni el detalle del proveedor. La llamada la hace
/// <see cref="LlamadaConsultas"/>, compartida con <see cref="ClienteRuntIntempo"/> (HU #13878).
/// </summary>
/// <example>
/// <code>
/// var respuesta = await cliente.LlamarAsync(celda.TenantId,
///     (c, opciones) => c.ConsultarMultasAsync(new ConsultarMultasRequest { Placa = placa }, opciones), ct);
/// var error = respuesta.Error ?? TraductorErroresConsultas.Traducir(respuesta.Respuesta!.Resultado);
/// </code>
/// </example>
public sealed class ClienteConsultas(ConsultasService.ConsultasServiceClient cliente, ILogger<ClienteConsultas> log)
{
    public const string Scope = "platform.consultas";
    public const string ClaveDireccion = "Diagnostico:Consultas:Address";
    public const string ClaveDeadline = "Diagnostico:Consultas:DeadlineSegundos";
    public const int DeadlinePorDefectoSegundos = 90;

    public Task<RespuestaConsultas<T>> LlamarAsync<T>(
        Guid empresa, Func<ConsultasService.ConsultasServiceClient, CallOptions, AsyncUnaryCall<T>> llamada, CancellationToken ct)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(llamada);
        return LlamadaConsultas.LlamarAsync(cliente, log, LlamadaConsultas.ConEmpresa(empresa, ct), llamada, ct);
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
        return LlamadaConsultas.LlamarAsync(cliente, log, new CallOptions(cancellationToken: ct), llamada, ct);
    }
}

/// <summary>
/// Puerta de Diagnóstico hacia <c>RuntIntempoService</c> de core-consultas (HU #13878, G1=a): el segundo cliente gRPC, hacia la
/// misma dirección (<see cref="ClienteConsultas.ClaveDireccion"/>) y con el mismo contrato que <see cref="ClienteConsultas.LlamarAsync"/>:
/// empresa obligatoria en <c>x-flit-tenant-id</c>, token de servicio con scope <see cref="ClienteConsultas.Scope"/>, deadline
/// común, <see cref="RpcException"/> traducido por <see cref="TraductorErroresConsultas"/> y un log con solo el método y el
/// código. Solo existe en <see cref="ModoAdaptador.Real"/>. La respuesta lleva PII (Q1=b): nunca se registra.
/// </summary>
/// <example>
/// <code>
/// var respuesta = await cliente.LlamarAsync(empresa,
///     (c, o) => c.ConsultarPorPlacaAsync(new ConsultarPorPlacaRequest { Placa = "TST601" }, o), ct);
/// </code>
/// </example>
public sealed class ClienteRuntIntempo(RuntIntempoService.RuntIntempoServiceClient cliente, ILogger<ClienteRuntIntempo> log)
{
    public Task<RespuestaConsultas<T>> LlamarAsync<T>(
        Guid empresa, Func<RuntIntempoService.RuntIntempoServiceClient, CallOptions, AsyncUnaryCall<T>> llamada, CancellationToken ct)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(llamada);
        return LlamadaConsultas.LlamarAsync(cliente, log, LlamadaConsultas.ConEmpresa(empresa, ct), llamada, ct);
    }
}

/// <summary>
/// La llamada común a core-consultas de <see cref="ClienteConsultas"/> y <see cref="ClienteRuntIntempo"/> (HU #13878: un solo
/// <c>try/catch</c>): devuelve la respuesta o el <see cref="RpcException"/> traducido con su código; la cancelación de quien llama
/// se propaga. El log lleva el tipo de respuesta, el código gRPC y el tipo de error; nunca <c>ex.Message</c> ni el payload.
/// </summary>
internal static partial class LlamadaConsultas
{
    /// <summary>Opciones con la empresa de la celda en <c>x-flit-tenant-id</c>; la empresa es obligatoria.</summary>
    public static CallOptions ConEmpresa(Guid empresa, CancellationToken ct)
    {
        if (empresa == Guid.Empty)
            throw new ArgumentException("La consulta exige la empresa de la celda.", nameof(empresa));

        return new CallOptions(new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, empresa.ToString() } }, cancellationToken: ct);
    }

    public static async Task<RespuestaConsultas<T>> LlamarAsync<TCliente, T>(
        TCliente cliente, ILogger log, CallOptions opciones, Func<TCliente, CallOptions, AsyncUnaryCall<T>> llamada, CancellationToken ct)
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

/// <summary>
/// Registro de los clientes gRPC de core-consultas (HU #13791; el de <c>RuntIntempoService</c> desde HU #13878): la misma
/// dirección, scope y deadline para los dos. Lo llama <c>Modulos.AddFuentesConsultas</c> en modo real.
/// </summary>
internal static class RegistroClienteConsultas
{
    public static IServiceCollection AddClienteConsultas(this IServiceCollection services, IConfiguration configuration, SeleccionAdaptadores seleccion)
    {
        ArgumentNullException.ThrowIfNull(seleccion);
        if (seleccion.Modo != ModoAdaptador.Real || seleccion.Direccion is null)
            throw new InvalidOperationException("El cliente de core-consultas solo se registra con adaptadores reales.");

        services.AddFlitGrpcClient<ConsultasService.ConsultasServiceClient>(
            configuration, seleccion.Direccion, ClienteConsultas.Scope, o => o.Deadline = seleccion.Deadline);
        services.AddFlitGrpcClient<RuntIntempoService.RuntIntempoServiceClient>(
            configuration, seleccion.Direccion, ClienteConsultas.Scope, o => o.Deadline = seleccion.Deadline);
        services.TryAddScoped<ClienteConsultas>();
        services.TryAddScoped<ClienteRuntIntempo>();
        return services;
    }
}

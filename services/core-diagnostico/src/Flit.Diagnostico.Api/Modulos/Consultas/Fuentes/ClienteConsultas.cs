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
/// Puerta de Diagnóstico hacia <c>ConsultasService</c> de core-consultas para lo que sigue en línea (acta §3.1; ADR-0075): desde
/// la tanda B las consultas de las celdas van por RabbitMQ y por aquí solo pasa el catálogo diario de departamentos con impuesto
/// (<c>ListarDepartamentosImpuesto</c>, HU #13861, <see cref="LlamarGlobalAsync"/>). Token de servicio de svc-diagnostico con scope
/// <see cref="Scope"/> y deadline <see cref="ClaveDeadline"/> (por defecto <see cref="DeadlinePorDefectoSegundos"/> s);
/// <see cref="LlamarAsync"/> pone la empresa en la metadata <c>x-flit-tenant-id</c> para un rpc por empresa. Un error gRPC se
/// devuelve traducido (<see cref="TraductorErroresConsultas"/>); la cancelación de quien llama se propaga. Solo se registra con
/// <see cref="ClaveDireccion"/>: sin ella ninguna llamada sale a core-consultas. Los logs llevan el método y el código; nunca
/// documentos ni el detalle del proveedor.
/// </summary>
/// <example>
/// <code>
/// var respuesta = await cliente.LlamarGlobalAsync(
///     (c, opciones) => c.ListarDepartamentosImpuestoAsync(new ListarDepartamentosImpuestoRequest(), opciones), ct);
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
/// La llamada a core-consultas de <see cref="ClienteConsultas"/> (un solo <c>try/catch</c>): devuelve la respuesta o el <see cref="RpcException"/> traducido con su código; la cancelación de quien llama
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
/// Registro del cliente gRPC de core-consultas (HU #13791; desde ADR-0075 solo para el catálogo de departamentos con impuesto):
/// con <see cref="ClienteConsultas.ClaveDireccion"/> (URL http o https) registra el cliente con el scope y el deadline; sin ella no
/// registra nada y devuelve false. Una dirección que no es URL o un deadline que no es positivo impiden arrancar. Lo llama
/// <c>Modulos.AddFuentesConsultas</c>.
/// </summary>
internal static class RegistroClienteConsultas
{
    public static bool AddClienteConsultas(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var segundos = configuration.GetValue(ClienteConsultas.ClaveDeadline, ClienteConsultas.DeadlinePorDefectoSegundos);
        if (segundos <= 0)
        {
            throw new InvalidOperationException(
                $"core-{ServicioSettings.Codigo} no puede arrancar: {Variable(ClienteConsultas.ClaveDeadline)} debe ser mayor que 0 (valor actual: {segundos}).");
        }

        var direccion = configuration[ClienteConsultas.ClaveDireccion]?.Trim();
        if (string.IsNullOrEmpty(direccion))
            return false;
        if (!Uri.TryCreate(direccion, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"core-{ServicioSettings.Codigo} no puede arrancar: {Variable(ClienteConsultas.ClaveDireccion)} no es una URL http o https " +
                "(p. ej. http://core-consultas:8084). Sin dirección, el catálogo de departamentos con impuesto queda sin capacidad.");
        }

        services.AddFlitGrpcClient<ConsultasService.ConsultasServiceClient>(
            configuration, uri, ClienteConsultas.Scope, o => o.Deadline = TimeSpan.FromSeconds(segundos));
        services.TryAddScoped<ClienteConsultas>();
        return true;
    }

    private static string Variable(string clave) => clave.Replace(":", "__", StringComparison.Ordinal);
}

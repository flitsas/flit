using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Consultas;

/// <summary>
/// Dónde está core-consultas y cuánto se le espera (sección <c>Consultas:Remoto</c>, la misma que usa core-api). Sin
/// dirección el portal arranca igual, pero toda consulta responde <c>proveedor_no_disponible</c>.
/// </summary>
public sealed class ConsultasOptions
{
    public const string SectionName = "Consultas:Remoto";

    /// <summary>URL h2c interna de core-consultas, por ejemplo <c>http://core-consultas:8084</c>.</summary>
    public string? Address { get; init; }

    /// <summary>
    /// Plazo de cada consulta. Un ciudadano esperando en una pantalla no aguanta los 180 s que usa core-api para el RUNT con
    /// su respaldo: pasado el plazo se responde <c>proveedor_no_disponible</c> y es él quien decide reintentar.
    /// </summary>
    public int DeadlineSegundos { get; init; } = 45;
}

/// <summary>
/// El proveedor de consultas no respondió (caído, plazo vencido, circuito abierto o sin configurar). El mensaje nunca lleva
/// datos de la consulta: viaja a logs y a la respuesta.
/// </summary>
public sealed class ProveedorNoDisponibleException(string message) : Exception(message);

/// <summary>Documento de identidad tal como lo entiende core-consultas: código del tipo y número sin puntos ni espacios.</summary>
public sealed record DocumentoConsulta(string Tipo, string Numero);

/// <summary>
/// Lo que el portal necesita de core-consultas. Devuelve el <see cref="ResultadoConsulta"/> CRUDO: quien lo recibe es la
/// capa de aplicación, que lo proyecta a dato mínimo y descarta el resto antes de cualquier log o respuesta.
/// </summary>
public interface IConsultasRunt
{
    /// <summary>Por VIN, o por placa con el documento del propietario (core-consultas exige el documento para la placa).</summary>
    /// <exception cref="ProveedorNoDisponibleException">core-consultas no respondió.</exception>
    Task<ResultadoConsulta> ConsultarVehiculoAsync(string? placa, string? vin, DocumentoConsulta? propietario, string? referencia, CancellationToken ct);

    /// <exception cref="ProveedorNoDisponibleException">core-consultas no respondió.</exception>
    Task<ResultadoConsulta> ConsultarConductorAsync(DocumentoConsulta documento, string? referencia, CancellationToken ct);

    /// <summary>
    /// Comparendos y acuerdos de pago. Fuente: <c>ConsultasService.ConsultarMultas</c>
    /// (<c>contracts/proto/flit/consultas/v1/consultas.proto</c>), atendido por core-consultas
    /// (<c>ConsultasGrpcService.ConsultarMultas</c>). Esa RPC es la consulta de multas que FLIT 2 ya usa
    /// (SIMIT o la fuente interna de la empresa). El BFF no tiene otro camino.
    /// </summary>
    /// <exception cref="ProveedorNoDisponibleException">core-consultas no respondió.</exception>
    Task<ResultadoConsulta> ConsultarMultasAsync(string? placa, DocumentoConsulta? documento, string? referencia, CancellationToken ct);
}

/// <summary>
/// Cliente gRPC hacia <c>ConsultasService</c> con el token de servicio del SDK (<c>svc-personanatural</c>, scope
/// <c>platform.consultas</c>) y la empresa «Persona Natural» en <c>x-flit-tenant-id</c>. El portal es anónimo: no hay
/// usuario del que el interceptor del SDK pueda sacar la empresa, así que va explícita en cada llamada.
/// </summary>
internal sealed partial class ConsultasRuntGrpc(ConsultasService.ConsultasServiceClient client, ILogger<ConsultasRuntGrpc> logger) : IConsultasRunt
{
    public Task<ResultadoConsulta> ConsultarVehiculoAsync(string? placa, string? vin, DocumentoConsulta? propietario, string? referencia, CancellationToken ct)
    {
        var request = new ConsultarVehiculoRequest { Opciones = Opciones(referencia) };
        if (!string.IsNullOrWhiteSpace(vin))
            request.Vin = new Vin { Valor = vin };
        if (!string.IsNullOrWhiteSpace(placa))
            request.Placa = new Placa { Valor = placa };
        if (propietario is not null)
            request.Propietario = new DocumentoIdentidad { Tipo = propietario.Tipo, Numero = propietario.Numero };

        return LlamarAsync(() => client.ConsultarVehiculoAsync(request, Cabeceras(), cancellationToken: ct).ResponseAsync, r => r.Resultado);
    }

    public Task<ResultadoConsulta> ConsultarConductorAsync(DocumentoConsulta documento, string? referencia, CancellationToken ct)
    {
        var request = new ConsultarConductorRequest
        {
            Opciones = Opciones(referencia),
            Documento = new DocumentoIdentidad { Tipo = documento.Tipo, Numero = documento.Numero },
        };
        return LlamarAsync(() => client.ConsultarConductorAsync(request, Cabeceras(), cancellationToken: ct).ResponseAsync, r => r.Resultado);
    }

    public Task<ResultadoConsulta> ConsultarMultasAsync(string? placa, DocumentoConsulta? documento, string? referencia, CancellationToken ct)
    {
        var request = new ConsultarMultasRequest { Opciones = Opciones(referencia), TipoPersona = TipoPersona.Natural };
        if (!string.IsNullOrWhiteSpace(placa))
            request.Placa = new Placa { Valor = placa };
        if (documento is not null)
            request.Documento = new DocumentoIdentidad { Tipo = documento.Tipo, Numero = documento.Numero };
        return LlamarAsync(() => client.ConsultarMultasAsync(request, Cabeceras(), cancellationToken: ct).ResponseAsync, r => r.Resultado);
    }

    /// <summary>
    /// Sin reintentos propios (el cliente del SDK se registra con <c>MaxRetries = 0</c>): un RUNT caído no se martilla desde
    /// aquí. Del <see cref="RpcException"/> solo se registra el estado, nunca su detalle.
    /// </summary>
    private async Task<ResultadoConsulta> LlamarAsync<TResponse>(Func<Task<TResponse>> llamada, Func<TResponse, ResultadoConsulta> resultado)
    {
        try
        {
            return resultado(await llamada().ConfigureAwait(false)) ?? throw new ProveedorNoDisponibleException("Consultas respondió sin resultado.");
        }
        catch (RpcException ex)
        {
            EstadoRecibido(logger, ex.StatusCode);
            throw new ProveedorNoDisponibleException($"core-consultas respondió {ex.StatusCode}.");
        }
        catch (InvalidOperationException)
        {
            // El token de servicio no se pudo obtener (Identidad caída o cliente de servicio mal configurado).
            SinToken(logger);
            throw new ProveedorNoDisponibleException("No se obtuvo el token de servicio para core-consultas.");
        }
    }

    // El consentimiento queda en true porque el endpoint público lo exige antes de llamar: sin él, core-consultas nunca
    // reutiliza la caché de una persona (ADR-0031) y cada consulta repetida pagaría al proveedor otra vez.
    private static OpcionesConsulta Opciones(string? referencia) => new()
    {
        ConsentimientoDatosPersonales = true,
        Referencia = referencia ?? string.Empty,
    };

    private static Metadata Cabeceras() => new() { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.PersonaNatural.ToString() } };

    [LoggerMessage(Level = LogLevel.Warning, Message = "core-consultas respondió {Estado}.")]
    private static partial void EstadoRecibido(ILogger logger, StatusCode estado);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se obtuvo el token de servicio para core-consultas.")]
    private static partial void SinToken(ILogger logger);
}

/// <summary>Reemplazo cuando no hay dirección de core-consultas (<c>Consultas:Remoto:Address</c>): toda consulta es «proveedor no disponible», no un error 500.</summary>
internal sealed class ConsultasRuntNoConfigurado : IConsultasRunt
{
    private const string Mensaje = $"No hay dirección de core-consultas ({ConsultasOptions.SectionName}:Address).";

    public Task<ResultadoConsulta> ConsultarVehiculoAsync(string? placa, string? vin, DocumentoConsulta? propietario, string? referencia, CancellationToken ct) =>
        throw new ProveedorNoDisponibleException(Mensaje);

    public Task<ResultadoConsulta> ConsultarConductorAsync(DocumentoConsulta documento, string? referencia, CancellationToken ct) =>
        throw new ProveedorNoDisponibleException(Mensaje);

    public Task<ResultadoConsulta> ConsultarMultasAsync(string? placa, DocumentoConsulta? documento, string? referencia, CancellationToken ct) =>
        throw new ProveedorNoDisponibleException(Mensaje);
}

/// <summary>Registro del cliente de core-consultas (HU #14039 y #14040).</summary>
public static class ConsultasRuntExtensions
{
    public const string Scope = "platform.consultas";

    internal static IServiceCollection AddConsultasRunt(this IServiceCollection services, IConfiguration configuration)
    {
        var opciones = configuration.GetSection(ConsultasOptions.SectionName).Get<ConsultasOptions>() ?? new ConsultasOptions();
        services.Configure<ConsultasOptions>(configuration.GetSection(ConsultasOptions.SectionName));

        if (Uri.TryCreate(opciones.Address, UriKind.Absolute, out var direccion))
        {
            // MaxRetries = 0: el contrato pide no reintentar en bucle ante un proveedor caído. El circuito del SDK se queda.
            services.AddFlitGrpcClient<ConsultasService.ConsultasServiceClient>(
                configuration, direccion, Scope, o =>
                {
                    o.Deadline = TimeSpan.FromSeconds(Math.Max(1, opciones.DeadlineSegundos));
                    o.MaxRetries = 0;
                });
            services.TryAddScoped<IConsultasRunt, ConsultasRuntGrpc>();
        }
        else
        {
            services.TryAddSingleton<IConsultasRunt, ConsultasRuntNoConfigurado>();
        }

        return services;
    }
}

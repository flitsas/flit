using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;

namespace Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;

/// <summary>
/// Cliente gRPC de <c>flit.platform.v1.ConfiguracionIaService</c> (ADR-0091 §1): al mismo puerto gRPC de core-api que el catálogo
/// (<c>Diagnostico:Tramites:Address</c>), con token de svc-diagnostico para el scope único <see cref="Scope"/> (SP-13: un token por
/// scope, patrón de <c>UsuariosPlataforma</c>). Sin <c>x-flit-tenant-id</c> propio: la configuración de IA es global.
/// </summary>
public static class ConfiguracionIaPlataforma
{
    /// <summary>Scope dedicado (audiencia <c>tramites</c>); solo svc-diagnostico lo tiene en Identidad.</summary>
    public const string Scope = "platform.ia.diagnostico";

    public static IHttpClientBuilder AddClienteConfiguracionIa(this IServiceCollection services, IConfiguration configuration, Uri direccion, TimeSpan plazo)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(direccion);
        return services.AddFlitGrpcClient<ConfiguracionIaService.ConfiguracionIaServiceClient>(configuration, direccion, Scope, o => o.Deadline = plazo);
    }
}

/// <summary>
/// Una línea al arrancar con el redactor elegido (SP-5, SP-16): origen, proveedor, kid y si la privada está presente; nunca la llave,
/// la privada ni su ruta. Con <c>plataforma</c> incompleta avisa «redactor sin configuración de plataforma» con un código fijo.
/// </summary>
public sealed partial class AvisoArranqueRedactor(AvisoArranqueRedactor.Estado estado, ILogger<AvisoArranqueRedactor> log) : IHostedService
{
    /// <summary>Lo que se decidió en el registro. <see cref="Motivo"/> es un código fijo (<c>OK</c>, <c>SIN_DIRECCION</c>, …).</summary>
    public sealed record Estado(string Origen, string Proveedor, string Kid, bool PrivadaPresente, string Motivo, string? AvisoAnterior);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var privada = estado.PrivadaPresente ? "presente" : "ausente";
        if (string.Equals(estado.Motivo, "OK", StringComparison.Ordinal))
            Elegido(log, estado.Origen, estado.Proveedor, string.IsNullOrEmpty(estado.Kid) ? "-" : estado.Kid, privada);
        else
            SinConfiguracion(log, estado.Origen, estado.Motivo, string.IsNullOrEmpty(estado.Kid) ? "-" : estado.Kid, privada);
        if (estado.AvisoAnterior is { } aviso)
            AnteriorIgnorada(log, aviso);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 126905, Level = LogLevel.Information,
        Message = "Redactor del dictamen: origen={Origen}, proveedor={Proveedor}, kid={Kid}, privada={Privada}")]
    private static partial void Elegido(ILogger logger, string origen, string proveedor, string kid, string privada);

    [LoggerMessage(EventId = 126906, Level = LogLevel.Warning,
        Message = "Redactor del dictamen sin configuración de plataforma (origen={Origen}, motivo={Motivo}, kid={Kid}, privada={Privada}): sale la plantilla")]
    private static partial void SinConfiguracion(ILogger logger, string origen, string motivo, string kid, string privada);

    [LoggerMessage(EventId = 126907, Level = LogLevel.Warning,
        Message = "Redactor del dictamen: la clave anterior de rotación se ignora ({Motivo})")]
    private static partial void AnteriorIgnorada(ILogger logger, string motivo);
}

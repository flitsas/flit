using Flit.Impuestos.Api.Persistence;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Impuestos.Api.Modulos.Ejecucion;

/// <summary>
/// Ritmo de la ejecución de liquidaciones (plan §1.4 DA-5, §2.2 y S-D), sección <see cref="Seccion"/>; todo se puede cambiar por
/// configuración (<c>Impuestos__Ejecucion__*</c>):
/// <list type="bullet">
///   <item>Crédito: <see cref="ConcurrenciaPorEmpresa"/> (2) ítems en vuelo por compañía y <see cref="ConcurrenciaGlobal"/> (6) en
///   total. En vuelo = <c>CONSULTANDO_RUNT</c> o <c>ENVIANDO</c>.</item>
///   <item>Reintentos del RUNT transitorio y del <c>rate_limited</c>: <see cref="EsperasReintento"/> (30 s, 2 min, 10 min) por
///   número de intento; el máximo de intentos es el del dominio (<c>TransicionesItem.MaximoIntentos</c>).</item>
///   <item>«No salió» (el POST no llegó al API): nuevo intento a los <see cref="EsperaNoSalio"/> (30 s), sin contar como intento.</item>
///   <item>Freno global: <see cref="TasaPorSegundo"/> POST por segundo y una pausa de <see cref="Pausa"/> tras
///   <see cref="TransitoriosParaPausa"/> fallos transitorios seguidos.</item>
///   <item>Primer GET de respaldo tras el 202: <see cref="PrimerPollConWebhook"/> (2 min) o <see cref="PrimerPollSinWebhook"/>
///   (10 s), según el calendario del plan §2.2; el resto del calendario es de la conciliación (M10).</item>
///   <item><see cref="Reconciliador"/>: cada cuánto se revisan los reintentos vencidos, los mensajes perdidos y el crédito.</item>
///   <item><see cref="PlazoRunt"/>: plazo del rpc del RUNT.</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// Impuestos__Ejecucion__ConcurrenciaPorEmpresa=2
/// Impuestos__Ejecucion__Reconciliador__Intervalo=00:00:15
/// </code>
/// </example>
public sealed class OpcionesEjecucion
{
    public const string Seccion = "Impuestos:Ejecucion";

    public int ConcurrenciaPorEmpresa { get; set; } = 2;

    public int ConcurrenciaGlobal { get; set; } = 6;

    public TimeSpan[] EsperasReintento { get; set; } = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    public TimeSpan EsperaNoSalio { get; set; } = TimeSpan.FromSeconds(30);

    public double TasaPorSegundo { get; set; } = 2;

    public int TransitoriosParaPausa { get; set; } = 5;

    public TimeSpan Pausa { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan PrimerPollConWebhook { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan PrimerPollSinWebhook { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan PlazoRunt { get; set; } = TimeSpan.FromSeconds(30);

    public OpcionesReconciliador Reconciliador { get; set; } = new();

    /// <summary>Espera antes del reintento número <paramref name="intento"/> (1 en adelante); desde el último, la última espera.</summary>
    public TimeSpan EsperaReintento(int intento)
    {
        var esperas = EsperasReintento is { Length: > 0 } e ? e : [TimeSpan.FromSeconds(30)];
        return esperas[Math.Clamp(intento - 1, 0, esperas.Length - 1)];
    }
}

/// <summary>Reconciliador de la ejecución (<see cref="ReconciliadorEjecucion"/>).</summary>
public sealed class OpcionesReconciliador
{
    /// <summary>Con false no corre como servicio en segundo plano (las pruebas lo llaman a mano).</summary>
    public bool Activo { get; set; } = true;

    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Un ítem en vuelo sin cambios desde hace más que esto perdió su mensaje y se vuelve a publicar.</summary>
    public TimeSpan Margen { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Ítems que se revisan por vuelta.</summary>
    public int Maximo { get; set; } = 200;
}

/// <summary>Registro de la ejecución de liquidaciones (M09): RUNT, despachador, freno, consumidor y reconciliador.</summary>
public static class EjecucionImpuestosExtensions
{
    /// <summary>Dirección del gRPC de core-consultas (<c>RuntIntempoService</c>).</summary>
    public const string ClaveDireccionConsultas = "Impuestos:Consultas:Address";

    /// <summary>Scope del token de <c>svc-impuestos</c> para el RUNT (core-consultas mide el consumo por producto con él).</summary>
    public const string ScopeConsultas = "platform.consultas";

    /// <summary>
    /// Con <see cref="ClaveDireccionConsultas"/>, el RUNT por gRPC; sin ella, un cliente que responde «transitorio» (los ítems terminan
    /// en error reintentable). Un registro previo de <see cref="IClienteRunt"/> (pruebas) se respeta. El consumidor atiende la cola
    /// <see cref="LiquidacionSolicitada.Cola"/> con <see cref="OpcionesEjecucion.ConcurrenciaGlobal"/> mensajes a la vez y solo
    /// mensajes del propio servicio.
    /// </summary>
    public static IServiceCollection AddEjecucionImpuestos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var seccion = configuration.GetSection(OpcionesEjecucion.Seccion);
        services.Configure<OpcionesEjecucion>(seccion);
        var opciones = seccion.Get<OpcionesEjecucion>() ?? new OpcionesEjecucion();
        if (configuration[ClaveDireccionConsultas] is { Length: > 0 } consultas)
        {
            services.AddFlitGrpcClient<Flit.Consultas.Grpc.V1.RuntIntempoService.RuntIntempoServiceClient>(
                configuration, new Uri(consultas), ScopeConsultas, o => o.Deadline = opciones.PlazoRunt);
            services.TryAddScoped<IClienteRunt, ClienteRuntGrpc>();
        }
        else
        {
            services.TryAddSingleton<IClienteRunt, ClienteRuntNoConectado>();
        }

        services.AddSingleton<GeneradorTokenAviso>();
        services.AddSingleton<FrenoGlobal>();
        services.AddScoped<DespachadorCredito>();
        services.AddSingleton<ReconciliadorEjecucion>();
        if (opciones.Reconciliador.Activo)
            services.AddHostedService(sp => sp.GetRequiredService<ReconciliadorEjecucion>());
        services.AddFlitConsumer<ImpuestosDb, ConsumidorLiquidacion, LiquidacionSolicitada>(
            configuration,
            LiquidacionSolicitada.Cola,
            LiquidacionSolicitada.Productor,
            [LiquidacionSolicitada.Tipo],
            o =>
            {
                o.Concurrencia = (ushort)Math.Clamp(opciones.ConcurrenciaGlobal, 1, ushort.MaxValue);
                o.ProductoresPermitidos.Add(LiquidacionSolicitada.Productor);
            });
        return services;
    }
}

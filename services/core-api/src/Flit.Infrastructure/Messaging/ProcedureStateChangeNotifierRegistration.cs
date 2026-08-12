using Flit.Infrastructure.Ict;
using Flit.Infrastructure.OtWebhooks;
using Flit.Tramites.Domain.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #11464 (Feature #11459, ADR-0045) — único punto que registra el fan-out de
/// <see cref="IProcedureStateChangeNotifier"/>. Hoy: OT solo, u OT+ICT. Mañana: + sink de correo
/// (HU #11465) sin volver a tocar dos rutas de DI.
/// </summary>
public static class ProcedureStateChangeNotifierRegistration
{
    /// <summary>
    /// Registra <see cref="IProcedureStateChangeNotifier"/> exactamente una vez.
    /// <paramref name="includeIctReflection"/> exige que <see cref="IctProcedureStateChangeNotifier"/>
    /// ya esté en el contenedor.
    /// </summary>
    public static IServiceCollection AddProcedureStateChangeNotifierFanOut(
        this IServiceCollection services,
        bool includeIctReflection)
    {
        services.AddScoped<IProcedureStateChangeNotifier>(sp =>
        {
            if (!includeIctReflection)
            {
                return sp.GetRequiredService<OtWebhookProcedureStateChangeNotifier>();
            }

            IProcedureStateChangeNotifier[] sinks =
            [
                sp.GetRequiredService<OtWebhookProcedureStateChangeNotifier>(),
                sp.GetRequiredService<IctProcedureStateChangeNotifier>(),
            ];
            return new CompositeProcedureStateChangeNotifier(
                sinks,
                sp.GetRequiredService<ILogger<CompositeProcedureStateChangeNotifier>>());
        });

        return services;
    }
}

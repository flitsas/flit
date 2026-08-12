using Flit.Infrastructure.Ict;
using Flit.Infrastructure.OtWebhooks;
using Flit.Tramites.Domain.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #11464 / #11465 (Feature #11459, ADR-0045) — único punto que registra el fan-out de
/// <see cref="IProcedureStateChangeNotifier"/>. Sinks: OT, opcionalmente ICT, y siempre el
/// sink de correo que encola despachos. Nunca registrar un
/// <c>AddScoped&lt;IProcedureStateChangeNotifier&gt;</c> fuera de aquí.
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
        services.AddScoped<ProcedureStateChangeEmailEnqueueNotifier>();

        services.AddScoped<IProcedureStateChangeNotifier>(sp =>
        {
            var sinks = new List<IProcedureStateChangeNotifier>
            {
                sp.GetRequiredService<OtWebhookProcedureStateChangeNotifier>(),
            };

            if (includeIctReflection)
            {
                sinks.Add(sp.GetRequiredService<IctProcedureStateChangeNotifier>());
            }

            sinks.Add(sp.GetRequiredService<ProcedureStateChangeEmailEnqueueNotifier>());

            if (sinks.Count == 1)
            {
                return sinks[0];
            }

            return new CompositeProcedureStateChangeNotifier(
                sinks.ToArray(),
                sp.GetRequiredService<ILogger<CompositeProcedureStateChangeNotifier>>());
        });

        return services;
    }
}

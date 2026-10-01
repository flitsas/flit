using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure;

/// <summary>
/// HU #10970 / #13143 — registro de la política <c>TramiteValidations</c> y su log de arranque.
/// </summary>
public static class TramiteValidationPolicyRegistration
{
    /// <summary>
    /// Registra la política YA resuelta (Application la consume sin <c>IOptions</c>) y un servicio de
    /// arranque que la resuelve al iniciar el host: el singleton es perezoso y, sin él, el log con los
    /// modos efectivos (y el aviso de valores no reconocidos) solo salía en la primera radicación.
    /// </summary>
    public static IServiceCollection AddTramiteValidationPolicy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TramiteValidationPolicyOptions>(
            configuration.GetSection(TramiteValidationPolicyOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TramiteValidationPolicyOptions>>().Value;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Flit.TramiteValidations");
            var policy = TramiteValidationPolicy.Resolve(
                options,
                (name, raw) => TramiteValidationLog.UnrecognizedMode(logger, name, raw));
            TramiteValidationLog.PolicyResolved(
                logger,
                policy.DuplicateActiveProcedure,
                policy.VehicleRegistrationState,
                policy.VehicleBodyTypeRequired,
                policy.MandatarioRequerido);
            return policy;
        });
        services.AddHostedService<TramiteValidationPolicyStartup>();
        return services;
    }

    private sealed class TramiteValidationPolicyStartup(TramiteValidationPolicy policy) : IHostedService
    {
        // Inyectar la política fuerza su resolución (y su log) al arrancar el host.
        private readonly TramiteValidationPolicy _policy = policy;

        public Task StartAsync(CancellationToken cancellationToken) =>
            _policy is null ? throw new InvalidOperationException() : Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

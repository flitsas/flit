using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #13350 (Epic #13316, ADR-0064) — Trámites publica sus eventos en <c>flit.tramites</c> por la outbox del SDK
/// (<c>tramites.outbox</c>, misma transacción que el cambio). Sección <c>Tramites:Bus</c>.
/// </summary>
public sealed class TramitesBusOptions
{
    public const string SectionName = "Tramites:Bus";

    /// <summary>Publica los eventos al broker. Apagado por defecto: sin broker en el ambiente no cambia nada.</summary>
    public bool Habilitado { get; set; }

    /// <summary>
    /// Sigue llenando <c>tramites.procedure_state_change_outbox</c>, de la que salen hoy el correo, el webhook del OT
    /// y el reflejo a ICT (AC2). Solo se apaga en el corte (#13359), cuando Notificaciones atienda el correo y el
    /// webhook desde el bus y el reflejo a ICT tenga su propio camino; apagarla antes los deja sin enviar.
    /// </summary>
    public bool EntregaEnProceso { get; set; } = true;
}

/// <summary>Tipos y datos de los eventos de Trámites, tal como los fija <c>contracts/asyncapi/tramites-events.v1.yaml</c>.</summary>
public static class TramitesEventos
{
    public const string Productor = "tramites";
    public const string EstadoCambiado = "tramites.procedure.state_changed";

    /// <summary><c>identity_validation.requested</c> → <c>tramites.identity_validation.requested</c>.</summary>
    public static string DeValidacion(IdentityValidationEvent evt) => $"{Productor}.{evt.EventType}";

    public static object Datos(TramiteTransitionRecord r) => new
    {
        procedureInstanceId = r.ProcedureInstanceId,
        fromStatus = r.FromStatus,
        toStatus = r.ToStatus,
        reason = r.Reason,
        changedByUserId = r.ChangedByUserId,
    };

    public static object Datos(IdentityValidationEvent evt) => evt switch
    {
        IdentityValidationRequested r => new
        {
            procedureInstanceId = r.ProcedureInstanceId,
            validationId = r.ValidationId,
            provider = r.Provider,
            parte = r.Parte,
            providerVerificationId = r.ProviderVerificationId,
        },
        IdentityValidationCompleted c => new
        {
            procedureInstanceId = c.ProcedureInstanceId,
            validationId = c.ValidationId,
            provider = c.Provider,
            parte = c.Parte,
            estado = c.Estado,
            providerStatus = c.ProviderStatus,
            score = c.Score,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(evt), evt.GetType().Name, "Evento de validación sin contrato en flit.tramites."),
    };
}

internal static class TramitesBusRegistration
{
    /// <summary>
    /// Registra las opciones y, con el bus encendido, la outbox del SDK sobre <see cref="FlitDbContext"/> y su
    /// publicador (<c>Platform:Messaging</c> con <c>Producer=tramites</c>). Falla al arrancar ante una combinación que
    /// perdería eventos.
    /// </summary>
    public static TramitesBusOptions AddTramitesBus(this IServiceCollection services, IConfiguration configuration, string identityValidationMessaging)
    {
        var options = new TramitesBusOptions();
        configuration.GetSection(TramitesBusOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        var rabbitIdentidad = string.Equals(identityValidationMessaging, "rabbitmq", StringComparison.OrdinalIgnoreCase);
        if (!options.Habilitado)
        {
            if (!options.EntregaEnProceso)
                throw new InvalidOperationException("Tramites:Bus:EntregaEnProceso=false exige Tramites:Bus:Habilitado=true: los cambios de estado no saldrían por ningún lado.");
            if (rabbitIdentidad)
                throw new InvalidOperationException("Messaging:IdentityValidation=rabbitmq exige Tramites:Bus:Habilitado=true y Platform:Messaging (RABBITMQ_URL_TRAMITES).");
            return options;
        }

        var producer = configuration[$"{PlatformMessagingOptions.SectionName}:Producer"];
        if (!string.Equals(producer, TramitesEventos.Productor, StringComparison.Ordinal))
            throw new InvalidOperationException($"Con Tramites:Bus:Habilitado, {PlatformMessagingOptions.SectionName}:Producer debe ser «{TramitesEventos.Productor}» (es «{producer}»).");

        services.AddFlitOutbox<FlitDbContext>(configuration);
        return options;
    }
}

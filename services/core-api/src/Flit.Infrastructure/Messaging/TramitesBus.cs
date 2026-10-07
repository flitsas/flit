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

    // HU #13359: la opción EntregaEnProceso se retiró. tramites.procedure_state_change_outbox se sigue llenando siempre:
    // de ella sale la orquestación del cambio de estado en core-api (destinatarios, correo armado y webhook firmado,
    // que se entregan a Notificaciones; reflejo a ICT por gRPC). Mover esa orquestación a consumidores del evento
    // tramites.procedure.state_changed queda como evolución, decisión de Samuel del 2026-10-07.
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

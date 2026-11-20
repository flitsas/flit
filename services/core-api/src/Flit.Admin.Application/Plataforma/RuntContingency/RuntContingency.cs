using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Microsoft.Extensions.Logging;

namespace Flit.Admin.Application.Plataforma.RuntContingency;

/// <summary>
/// Épica #14644 (HU #14648) — vocabulario de la contingencia de proveedor RUNT: los tipos de consulta y los proveedores que
/// puede forzar cada uno. Es el mismo que valida core-consultas (que es quien la aplica); aquí se valida primero para
/// responder 400 con el campo exacto sin ir al servicio.
/// </summary>
public static class RuntContingencyCatalog
{
    public const string VehicleVin = "vehicle_vin";
    public const string VehiclePlate = "vehicle_plate";
    public const string Conductor = "conductor";

    public const int ReasonMaxLength = 500;

    /// <summary>Los tres tipos, en el orden en que se muestran.</summary>
    public static IReadOnlyList<string> Kinds { get; } = [VehicleVin, VehiclePlate, Conductor];

    public static bool IsKind(string? kind) => kind is not null && Kinds.Contains(kind, StringComparer.Ordinal);

    /// <summary>Proveedores que puede forzar el tipo: vehículo {kyverum_runt, verifik}; conductor {kyverum_runt_conductor, verifik_conductor}.</summary>
    public static IReadOnlyList<string> ProvidersFor(string? kind) => kind switch
    {
        VehicleVin or VehiclePlate => ["kyverum_runt", "verifik"],
        Conductor => ["kyverum_runt_conductor", "verifik_conductor"],
        _ => [],
    };
}

/// <summary>Estado de un tipo. <see cref="Provider"/> nulo = «Sin contingencia»; el resto es del último cambio (nulo si nunca hubo uno).</summary>
public sealed record RuntContingencyItem(string Kind, string? Provider, string? Reason, string? UpdatedByName, DateTimeOffset? UpdatedAt);

/// <summary>core-consultas no respondió: el endpoint lo traduce en 503.</summary>
public sealed class RuntContingencyUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Puerto hacia core-consultas, dueño de la contingencia y de aplicarla (gRPC <c>ConsultasAdminService</c>).</summary>
public interface IRuntContingencyGateway
{
    /// <summary>Los tres tipos, siempre, en el orden de <see cref="RuntContingencyCatalog.Kinds"/>.</summary>
    /// <exception cref="RuntContingencyUnavailableException">core-consultas no respondió.</exception>
    Task<IReadOnlyList<RuntContingencyItem>> ListAsync(CancellationToken cancellationToken);

    /// <exception cref="RuntContingencyUnavailableException">core-consultas no respondió.</exception>
    Task<RuntContingencyItem> SaveAsync(string kind, string? provider, string reason, string actorName, Guid? actorUserId, CancellationToken cancellationToken);
}

public sealed class GetRuntContingencyHandler(IRuntContingencyGateway gateway)
{
    public async Task<IReadOnlyList<RuntContingencyItem>> HandleAsync(CancellationToken cancellationToken) =>
        await gateway.ListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed record SaveRuntContingencyCommand(string Kind, string? Provider, string? Reason, Guid? ActorUserId, string? ActorName);

public enum SaveRuntContingencyOutcome
{
    Ok,
    KindNotFound,
    Invalid,
}

public sealed record SaveRuntContingencyResult(
    SaveRuntContingencyOutcome Outcome,
    RuntContingencyItem? Item,
    IReadOnlyDictionary<string, string[]> Errors)
{
    internal static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();
}

/// <summary>
/// Activa, cambia o apaga la contingencia de un tipo (HU #14648). Valida, guarda en core-consultas y deja el cambio en la
/// auditoría de PLATAFORMA (<c>TenantId = null</c>: ningún historial de empresa registra nada) con quién, cuándo, el motivo y los
/// valores anterior y nuevo. Nada cambia si la validación falla.
/// </summary>
public sealed partial class SaveRuntContingencyHandler(IRuntContingencyGateway gateway, IAdminAuditWriter auditWriter, ILogger<SaveRuntContingencyHandler> logger)
{
    public const string ReasonRequiredMessage = "Escribe el motivo del cambio.";
    public const string ProviderInvalidMessage = "Ese proveedor no atiende este tipo de consulta.";
    public const string EntityName = "runt_contingency";

    public async Task<SaveRuntContingencyResult> HandleAsync(SaveRuntContingencyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!RuntContingencyCatalog.IsKind(command.Kind))
            return new SaveRuntContingencyResult(SaveRuntContingencyOutcome.KindNotFound, null, SaveRuntContingencyResult.NoErrors);

        var errors = new Dictionary<string, string[]>();
        var provider = string.IsNullOrWhiteSpace(command.Provider) ? null : command.Provider.Trim();
        if (provider is not null && !RuntContingencyCatalog.ProvidersFor(command.Kind).Contains(provider, StringComparer.Ordinal))
            errors["provider"] = [ProviderInvalidMessage];

        var reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
            errors["reason"] = [ReasonRequiredMessage];
        else if (reason.Length > RuntContingencyCatalog.ReasonMaxLength)
            errors["reason"] = [$"El motivo admite hasta {RuntContingencyCatalog.ReasonMaxLength} caracteres."];

        if (errors.Count > 0)
            return new SaveRuntContingencyResult(SaveRuntContingencyOutcome.Invalid, null, errors);

        var before = (await gateway.ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(i => i.Kind == command.Kind);
        var actorName = string.IsNullOrWhiteSpace(command.ActorName) ? "Super Admin FLIT" : command.ActorName.Trim();
        var saved = await gateway.SaveAsync(command.Kind, provider, reason, actorName, command.ActorUserId, cancellationToken).ConfigureAwait(false);

        try
        {
            await auditWriter.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                Module: AuditVocabulary.Modules.Config,
                EntityName: EntityName,
                Operation: AuditVocabulary.Operations.Update,
                Result: AuditVocabulary.Results.Success,
                ErrorCode: null,
                ActorUserId: command.ActorUserId,
                TargetEntityType: "RUNT_CONTINGENCY",
                TargetEntityId: null,
                ClientIp: null,
                UserAgent: null,
                OldValue: Serialize(command.Kind, before?.Provider, before?.Reason),
                NewValue: Serialize(command.Kind, saved.Provider, reason)),
            cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El cambio ya está guardado en core-consultas: la auditoría fallida no lo revierte ni rompe la respuesta. Sin el motivo en el log.
            AuditoriaFallida(command.Kind, command.ActorUserId, ex);
        }

        return new SaveRuntContingencyResult(SaveRuntContingencyOutcome.Ok, saved, SaveRuntContingencyResult.NoErrors);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "No se pudo escribir la auditoría de Plataforma del cambio de contingencia RUNT (tipo={Kind}, actor={ActorUserId}).")]
    private partial void AuditoriaFallida(string kind, Guid? actorUserId, Exception ex);

    private static string Serialize(string kind, string? provider, string? reason)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", kind);
            writer.WriteString("provider", provider);
            writer.WriteString("reason", reason);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}

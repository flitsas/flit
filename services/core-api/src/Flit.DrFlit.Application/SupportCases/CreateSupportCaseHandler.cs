using System.Net.Mail;
using Flit.DrFlit.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Flit.DrFlit.Application.SupportCases;

/// <summary>Formulario de caso confirmado por el usuario (ADR-0060 §5.2). Nunca trae nada del chat.</summary>
public sealed record CreateSupportCaseCommand(
    Guid TenantId,
    Guid UserId,
    bool CallerIsSuperAdmin,
    string? Nombre,
    string? Email,
    string? Telefono,
    string? Compania,
    string? Detalle,
    string? ResultadoEsperado,
    string? Frecuencia,
    string? Titulo,
    string? Prioridad,
    string? AffectedModule,
    IReadOnlyList<Guid>? AttachmentIds);

public enum CreateSupportCaseOutcome
{
    Created,
    Invalid,

    /// <summary>El sistema de soporte no respondió tras el reintento: la fila queda <c>failed</c>.</summary>
    ProviderUnavailable,
}

/// <param name="CaseId">Id del work item: es el número de caso que ve el usuario.</param>
/// <param name="CaseUrl">Solo para SuperAdmin (decisión de la épica: el cliente ve el número, no el enlace).</param>
public sealed record CreateSupportCaseResult(
    CreateSupportCaseOutcome Outcome,
    int? CaseId = null,
    string? CaseUrl = null,
    int AttachmentsFailed = 0,
    string? Error = null);

/// <summary>
/// Radica un caso de soporte de DR. FLIT (HU #12925, ADR-0060 §7.2):
/// <list type="number">
///   <item>valida el formulario y que los adjuntos sean del usuario, estén vigentes y no pasen del tope;</item>
///   <item>persiste el intento en <c>pending</c>;</item>
///   <item>el gateway sube los adjuntos (no bloqueantes) y crea el Bug;</item>
///   <item>marca <c>created</c> y vincula los adjuntos, o <c>failed</c> con el código del error.</item>
/// </list>
/// Si falla, los adjuntos quedan pendientes: el usuario reintenta desde el mismo formulario sin volver a
/// subirlos. No hay reconciliación en segundo plano (fuera de alcance de v1).
/// </summary>
public sealed class CreateSupportCaseHandler(
    IDrFlitSupportCaseGateway gateway,
    IDrFlitSupportCaseRepository repository,
    IDrFlitSupportAttachmentStore attachments,
    IDrFlitSupportCaseSettings settings,
    TimeProvider clock,
    ILogger<CreateSupportCaseHandler> logger)
{
    public const int MaxNombre = 200;
    public const int MaxTelefono = 30;
    public const int MaxCompania = 200;
    public const int MaxDetalle = 4000;
    public const int MaxResultadoEsperado = 2000;
    public const int MaxTitulo = 200;

    /// <summary>Mensaje del 502: el frontend lo acompaña con los canales de soporte estáticos.</summary>
    public const string ProviderUnavailableMessage =
        "No pudimos radicar tu caso en este momento. Intenta de nuevo en unos minutos o escríbenos por los canales de soporte.";

    public async Task<CreateSupportCaseResult> HandleAsync(CreateSupportCaseCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var error = Validate(command, out var priority, out var frequency);
        if (error is not null)
            return new(CreateSupportCaseOutcome.Invalid, Error: error);

        var ids = (command.AttachmentIds ?? []).Distinct().ToList();
        if (ids.Count > settings.MaxAttachments)
            return new(CreateSupportCaseOutcome.Invalid, Error: $"Puedes adjuntar máximo {settings.MaxAttachments} archivos.");

        var now = clock.GetUtcNow();
        var pending = await attachments.GetPendingAsync(command.TenantId, command.UserId, ids, now, ct).ConfigureAwait(false);
        if (pending.Count != ids.Count)
            return new(CreateSupportCaseOutcome.Invalid, Error: "Uno o más adjuntos ya no están disponibles. Vuelve a subirlos.");

        var ticket = new DrFlitSupportTicket(
            Title: command.Titulo!.Trim(),
            RequesterName: command.Nombre!.Trim(),
            RequesterEmail: command.Email!.Trim(),
            RequesterPhone: string.IsNullOrWhiteSpace(command.Telefono) ? null : command.Telefono.Trim(),
            Company: command.Compania!.Trim(),
            Detail: command.Detalle!.Trim(),
            ExpectedResult: command.ResultadoEsperado!.Trim(),
            Frequency: frequency,
            Priority: priority,
            Environment: settings.DeployEnvironment,
            AffectedModule: command.AffectedModule,
            ReportedAt: now);

        var caseId = await repository.InsertPendingAsync(
            new DrFlitSupportCaseRecord(
                command.TenantId, command.UserId, gateway.DestinationName, ticket, gateway.ResolveAffectedModule(command.AffectedModule)),
            ct).ConfigureAwait(false);

        var files = pending
            .Select(p => new DrFlitTicketAttachment(p.FileName, p.ContentType, c => attachments.OpenAsync(p.StoragePath, c)))
            .ToList();
        var result = await gateway.CreateBugAsync(ticket, files, ct).ConfigureAwait(false);

        if (!result.Created || result.WorkItemId is null)
        {
            var code = result.ErrorCode ?? "unknown";
            await repository.MarkFailedAsync(caseId, code, pending.Count, result.AttachmentsFailed, ct).ConfigureAwait(false);
            SupportCaseLog.Failed(logger, command.TenantId, command.UserId, caseId, code);
            return new(CreateSupportCaseOutcome.ProviderUnavailable, AttachmentsFailed: result.AttachmentsFailed, Error: ProviderUnavailableMessage);
        }

        await repository.MarkCreatedAsync(caseId, result.WorkItemId.Value, pending.Count, result.AttachmentsFailed, ct).ConfigureAwait(false);
        await attachments.LinkToCaseAsync(pending.Select(p => p.Id).ToList(), caseId, ct).ConfigureAwait(false);
        SupportCaseLog.Created(logger, command.TenantId, command.UserId, caseId, result.WorkItemId.Value, result.AttachmentsFailed);

        return new(
            CreateSupportCaseOutcome.Created,
            result.WorkItemId,
            command.CallerIsSuperAdmin ? result.WorkItemUrl : null,
            result.AttachmentsFailed);
    }

    /// <summary>Campos obligatorios, longitudes, correo y enums. Devuelve el motivo o <c>null</c>.</summary>
    internal static string? Validate(CreateSupportCaseCommand c, out DrFlitCasePriority priority, out DrFlitCaseFrequency frequency)
    {
        priority = default;
        frequency = default;

        static string? Required(string? value, string label, int max) =>
            string.IsNullOrWhiteSpace(value) ? $"Falta {label}."
            : value.Trim().Length > max ? $"{char.ToUpperInvariant(label[0])}{label[1..]} supera {max} caracteres."
            : null;

        var error = Required(c.Nombre, "el nombre", MaxNombre)
            ?? Required(c.Email, "el correo", 320)
            ?? Required(c.Compania, "la compañía", MaxCompania)
            ?? Required(c.Detalle, "el detalle del error", MaxDetalle)
            ?? Required(c.ResultadoEsperado, "el resultado esperado", MaxResultadoEsperado)
            ?? Required(c.Titulo, "el título", MaxTitulo);
        if (error is not null)
            return error;

        if (!IsEmail(c.Email!.Trim()))
            return "El correo no es válido.";
        if (c.Telefono is { } phone && phone.Trim().Length > MaxTelefono)
            return $"El teléfono supera {MaxTelefono} caracteres.";
        if (!DrFlitSupportCaseWire.TryParsePriority(c.Prioridad, out priority))
            return "La prioridad debe ser Alta, Media o Baja.";
        if (!DrFlitSupportCaseWire.TryParseFrequency(c.Frecuencia, out frequency))
            return "La frecuencia debe ser una_vez, a_veces o siempre.";

        return null;
    }

    private static bool IsEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase) && value.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Logging source-generated (CA1848). Sin datos de contacto ni texto del caso.</summary>
internal static partial class SupportCaseLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "DR. FLIT soporte: caso {SupportCaseId} radicado como work item {WorkItemId} (tenant {TenantId}, user {UserId}, adjuntos fallidos {AttachmentsFailed})")]
    public static partial void Created(ILogger logger, Guid tenantId, Guid userId, Guid supportCaseId, int workItemId, int attachmentsFailed);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DR. FLIT soporte: caso {SupportCaseId} no radicado ({ErrorCode}) (tenant {TenantId}, user {UserId})")]
    public static partial void Failed(ILogger logger, Guid tenantId, Guid userId, Guid supportCaseId, string errorCode);
}

using System.Net.Mail;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Security.Application.Auth.BulkInvitation;

/// <summary>Estado de una fila en el reporte (valores estables de cara al cliente).</summary>
public static class BulkInvitationRowStatus
{
    /// <summary>dryRun: la fila pasó todas las validaciones y se creará en el commit.</summary>
    public const string Valid = "valid";

    /// <summary>commit: se creó la invitación.</summary>
    public const string Created = "created";

    /// <summary>La fila se rechazó; <c>ErrorCode</c> dice por qué. Las demás filas siguen.</summary>
    public const string Rejected = "rejected";

    /// <summary>commit: se alcanzó el deadline antes de procesar la fila (no se creó nada).</summary>
    public const string NotProcessed = "not_processed";
}

/// <summary>Códigos ESTABLES de rechazo por fila (los de rol los define <see cref="InvitationScopeReasons"/>).</summary>
public static class BulkInvitationRowErrors
{
    public const string FullNameRequired = "FULL_NAME_REQUIRED";
    public const string FullNameTooLong = "FULL_NAME_TOO_LONG";
    public const string EmailRequired = "EMAIL_REQUIRED";
    public const string EmailInvalid = "EMAIL_INVALID";
    public const string RoleRequired = "ROLE_REQUIRED";
    public const string CellFormulaNotAllowed = "CELL_FORMULA_NOT_ALLOWED";
    public const string DuplicateEmailInFile = "DUPLICATE_EMAIL_IN_FILE";

    /// <summary>HU #11580: único código de cara al cliente para correo con cuenta, invitación pendiente o cuenta eliminada.</summary>
    public const string EmailAlreadyInUse = "EMAIL_ALREADY_IN_USE";

    /// <summary>Falla inesperada en la fila; el detalle queda solo en el log (sin PII).</summary>
    public const string RowFailed = "ROW_FAILED";
}

public sealed record BulkInvitationRowResult(
    int RowNumber,
    string FullName,
    string Email,
    string Role,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    bool? EmailSent);

/// <param name="DryRun"><c>true</c> = solo validó; no se creó nada, no se envió correo y no se guardó el lote.</param>
/// <param name="Total">Filas de datos del archivo.</param>
/// <param name="Valid">dryRun: filas que se crearían. En commit: las que pasaron la validación previa.</param>
/// <param name="Created">Invitaciones creadas (siempre 0 en dryRun).</param>
/// <param name="Rejected">Filas rechazadas (validación o handler).</param>
/// <param name="NotProcessed">Filas válidas que el deadline dejó sin procesar.</param>
/// <param name="Rows">Detalle por fila, en el orden del archivo.</param>
public sealed record BulkInvitationReport(
    bool DryRun,
    int Total,
    int Valid,
    int Created,
    int Rejected,
    int NotProcessed,
    IReadOnlyList<BulkInvitationRowResult> Rows);

/// <param name="FileError">Código de <see cref="BulkInvitationFileErrors"/> si el archivo completo se rechazó.</param>
/// <param name="Report">Reporte por fila cuando el archivo se procesó.</param>
public sealed record BulkInvitationResult(string? FileError, BulkInvitationReport? Report);

/// <summary>
/// Invitación masiva en dos fases SIN ESTADO (HU #14423, ADR-0077): <c>dryRun=true</c> valida y previsualiza;
/// <c>dryRun=false</c> revalida TODO (no confía en el dryRun previo: el archivo pudo cambiar) y crea cada invitación con
/// <see cref="CreateInvitationHandler"/> en su propio scope DI, para aislar el <c>DbContext</c> ante una violación de unicidad.
/// No persiste el lote ni sus filas; la auditoría resumen la escribe el endpoint.
/// <para>
/// <see cref="IInvitationScopePolicy"/> se evalúa SIEMPRE antes del handler, y el commit no usa <c>RequestAborted</c> sino un
/// deadline propio: una desconexión del cliente no debe dejar el lote a medias.
/// </para>
/// </summary>
public sealed partial class BulkInvitationService(
    IInvitationScopePolicy scopePolicy,
    IBulkInvitationXlsxParser parser,
    IInvitationRepository invitationRepository,
    IServiceScopeFactory scopeFactory,
    BulkInvitationOptions options,
    ILogger<BulkInvitationService> logger)
{
    private const int FullNameMaxLength = 200;
    private const int EmailMaxLength = 320;

    /// <exception cref="InvitationScopeDeniedException">El alcance del invitador no permite la operación.</exception>
    public async Task<BulkInvitationResult> ProcessAsync(
        InvitationScopeContext scope,
        Stream file,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(file);

        // 1) Alcance ANTES que cualquier otra cosa: sin permiso no se lee ni una fila.
        var decision = await scopePolicy.ResolveAsync(scope, cancellationToken).ConfigureAwait(false);
        if (!decision.Allowed)
        {
            throw new InvitationScopeDeniedException(decision.DenyReason ?? InvitationScopeReasons.InviterNotAuthorized);
        }

        // 2) Archivo completo: cualquier problema lo rechaza sin procesar nada.
        var parsed = parser.Parse(file);
        if (parsed.FileError is not null)
        {
            return new BulkInvitationResult(parsed.FileError, null);
        }

        // 3) Validación fila por fila (misma en dryRun y commit).
        var rows = await ValidateAsync(decision, parsed.Rows, cancellationToken).ConfigureAwait(false);
        var validCount = rows.Count(r => r.Status == BulkInvitationRowStatus.Valid);

        if (dryRun)
        {
            return new BulkInvitationResult(null, BuildReport(dryRun: true, validCount, rows));
        }

        // 4) Commit: solo las filas válidas llegan al handler. La decisión (y con ella el id de cada rol) sale de la
        // política evaluada en ESTA petición; nada se arrastra de un dryRun anterior.
        var committed = await CommitAsync(scope, decision, rows).ConfigureAwait(false);
        return new BulkInvitationResult(null, BuildReport(dryRun: false, validCount, committed));
    }

    private async Task<BulkInvitationRowResult[]> ValidateAsync(
        InvitationScopeDecision decision,
        IReadOnlyList<BulkInvitationParsedRow> parsedRows,
        CancellationToken cancellationToken)
    {
        var results = new BulkInvitationRowResult[parsedRows.Count];
        var seenEmails = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < parsedRows.Count; i++)
        {
            var row = parsedRows[i];
            var email = EmailNormalizer.Normalize(row.Email);

            // El duplicado se marca por correo normalizado, sin importar si la primera aparición era válida.
            var isDuplicate = email.Length > 0 && !seenEmails.Add(email);

            var error = await ValidateRowAsync(decision, row, email, isDuplicate, cancellationToken).ConfigureAwait(false);

            results[i] = error is null
                ? ToResult(row, email, BulkInvitationRowStatus.Valid, null, null)
                : ToResult(row, email, BulkInvitationRowStatus.Rejected, error, MessageFor(error));
        }

        return results;
    }

    private async Task<string?> ValidateRowAsync(
        InvitationScopeDecision decision,
        BulkInvitationParsedRow row,
        string email,
        bool isDuplicate,
        CancellationToken cancellationToken)
    {
        if (row.FormulaNeutralized)
        {
            return BulkInvitationRowErrors.CellFormulaNotAllowed;
        }

        if (string.IsNullOrWhiteSpace(row.FullName))
        {
            return BulkInvitationRowErrors.FullNameRequired;
        }

        if (row.FullName.Length > FullNameMaxLength)
        {
            return BulkInvitationRowErrors.FullNameTooLong;
        }

        if (email.Length == 0)
        {
            return BulkInvitationRowErrors.EmailRequired;
        }

        if (!IsValidEmail(email))
        {
            return BulkInvitationRowErrors.EmailInvalid;
        }

        if (string.IsNullOrWhiteSpace(row.Role))
        {
            return BulkInvitationRowErrors.RoleRequired;
        }

        if (isDuplicate)
        {
            return BulkInvitationRowErrors.DuplicateEmailInFile;
        }

        // Rol: la política decide. Un rol que existe pero no es asignable responde con SU motivo (SuperAdmin, tipo de
        // entidad, administrador); uno que no existe en el destino, ROLE_NOT_FOUND.
        var matches = decision.FindRoles(row.Role);
        if (matches.Count == 0)
        {
            return InvitationScopeReasons.RoleNotFound;
        }

        if (matches.Count > 1)
        {
            return InvitationScopeReasons.RoleAmbiguous;
        }

        if (!matches[0].Assignable)
        {
            return matches[0].DenyReason ?? InvitationScopeReasons.RoleNotFound;
        }

        // HU #11580: cuenta existente, invitación pendiente (en cualquier compañía) o cuenta eliminada responden UN solo
        // código; la causa no se distingue de cara al cliente.
        if (await invitationRepository.ExistsPendingAsync(email, cancellationToken).ConfigureAwait(false)
            || await invitationRepository.UserExistsWithEmailAsync(email, cancellationToken).ConfigureAwait(false))
        {
            return BulkInvitationRowErrors.EmailAlreadyInUse;
        }

        return null;
    }

    private async Task<BulkInvitationRowResult[]> CommitAsync(
        InvitationScopeContext scope,
        InvitationScopeDecision decision,
        BulkInvitationRowResult[] validated)
    {
        var pending = new List<(int Index, Guid RoleId)>();
        for (var i = 0; i < validated.Length; i++)
        {
            if (validated[i].Status != BulkInvitationRowStatus.Valid)
            {
                continue;
            }

            var matches = decision.FindRoles(validated[i].Role);
            if (matches.Count == 1 && matches[0].Assignable)
            {
                pending.Add((i, matches[0].Id));
            }
            else
            {
                // Cambió entre la validación y aquí: fail-closed.
                validated[i] = validated[i] with
                {
                    Status = BulkInvitationRowStatus.Rejected,
                    ErrorCode = matches.Count == 1 ? matches[0].DenyReason : InvitationScopeReasons.RoleNotFound,
                    ErrorMessage = MessageFor(matches.Count == 1 ? matches[0].DenyReason : InvitationScopeReasons.RoleNotFound),
                };
            }
        }

        // Deadline propio: NO se enlaza con RequestAborted (una desconexión no debe cortar el lote a la mitad).
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(0, options.CommitDeadlineSeconds)));

        try
        {
            await Parallel.ForEachAsync(
                pending,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelism),
                    CancellationToken = deadline.Token,
                },
                async (item, _) =>
                {
                    // Cada fila escribe SOLO su posición del arreglo: sin contención. Una fila ya iniciada termina
                    // siempre (token None): si no, quedaría la invitación creada y el correo sin enviar.
                    validated[item.Index] = await CreateOneAsync(scope, validated[item.Index], item.RoleId).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            LogDeadlineReached(logger, options.CommitDeadlineSeconds);
        }

        // Lo que quedó «valid» tras el commit nunca llegó al handler: se reporta como no procesado.
        for (var i = 0; i < validated.Length; i++)
        {
            if (validated[i].Status == BulkInvitationRowStatus.Valid)
            {
                validated[i] = validated[i] with
                {
                    Status = BulkInvitationRowStatus.NotProcessed,
                    ErrorMessage = "La carga alcanzó el tiempo máximo antes de procesar esta fila. Vuelve a cargar el archivo.",
                };
            }
        }

        return validated;
    }

    private async Task<BulkInvitationRowResult> CreateOneAsync(
        InvitationScopeContext scope,
        BulkInvitationRowResult row,
        Guid roleId)
    {
        try
        {
            await using var rowScope = scopeFactory.CreateAsyncScope();
            var handler = rowScope.ServiceProvider.GetRequiredService<CreateInvitationHandler>();

            var created = await handler.HandleAsync(
                new CreateInvitationCommand(scope.TargetTenantId, row.Email, row.FullName, [roleId], scope.InviterUserId),
                CancellationToken.None).ConfigureAwait(false);

            return row with { Status = BulkInvitationRowStatus.Created, EmailSent = created.EmailSent };
        }
        catch (Exception ex) when (ex is InvitationAlreadyPendingException or UserAlreadyExistsException)
        {
            // Carrera entre filas/cargas o reenvío del mismo archivo (AC5): el mismo código único de cara al cliente.
            return Rejected(row, BulkInvitationRowErrors.EmailAlreadyInUse);
        }
        catch (RoleNotFoundException)
        {
            return Rejected(row, InvitationScopeReasons.RoleNotFound);
        }
        catch (NoRolesSelectedException)
        {
            return Rejected(row, BulkInvitationRowErrors.RoleRequired);
        }
#pragma warning disable CA1031 // Una fila fallida no puede tumbar el lote: se reporta y las demás siguen.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Solo el tipo y el número de fila: el mensaje de una DbUpdateException puede traer el correo (PII).
            LogRowFailed(logger, row.RowNumber, ex.GetType().Name);
            return Rejected(row, BulkInvitationRowErrors.RowFailed);
        }
    }

    private static BulkInvitationRowResult Rejected(BulkInvitationRowResult row, string code) =>
        row with { Status = BulkInvitationRowStatus.Rejected, ErrorCode = code, ErrorMessage = MessageFor(code) };

    private static BulkInvitationRowResult ToResult(
        BulkInvitationParsedRow row, string normalizedEmail, string status, string? code, string? message) =>
        new(
            row.RowNumber,
            row.FullName,
            // Se devuelve el correo normalizado (el que se invitaría); si estaba vacío o roto, lo escrito.
            normalizedEmail.Length > 0 ? normalizedEmail : row.Email,
            row.Role,
            status,
            code,
            message,
            null);

    private static BulkInvitationReport BuildReport(bool dryRun, int validCount, BulkInvitationRowResult[] rows)
    {
        var created = rows.Count(r => r.Status == BulkInvitationRowStatus.Created);
        var rejected = rows.Count(r => r.Status == BulkInvitationRowStatus.Rejected);
        var notProcessed = rows.Count(r => r.Status == BulkInvitationRowStatus.NotProcessed);

        return new BulkInvitationReport(dryRun, rows.Length, validCount, created, rejected, notProcessed, rows);
    }

    private static bool IsValidEmail(string email) =>
        email.Length <= EmailMaxLength
        && email.IndexOf('@', StringComparison.Ordinal) > 0
        && email.IndexOf(' ', StringComparison.Ordinal) < 0
        && MailAddress.TryCreate(email, out var parsed)
        && string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase)
        && parsed.Host.Contains('.', StringComparison.Ordinal);

    private static string MessageFor(string? code) => code switch
    {
        BulkInvitationRowErrors.FullNameRequired => "El nombre completo es obligatorio.",
        BulkInvitationRowErrors.FullNameTooLong => "El nombre completo supera los 200 caracteres.",
        BulkInvitationRowErrors.EmailRequired => "El correo es obligatorio.",
        BulkInvitationRowErrors.EmailInvalid => "El correo no tiene un formato válido.",
        BulkInvitationRowErrors.RoleRequired => "Elige el rol de la persona.",
        BulkInvitationRowErrors.CellFormulaNotAllowed => "La fila contiene una celda que empieza por =, +, - o @; no se admiten fórmulas.",
        BulkInvitationRowErrors.DuplicateEmailInFile => "El correo está repetido en el archivo.",
        BulkInvitationRowErrors.EmailAlreadyInUse => "El correo ya está en uso.",
        BulkInvitationRowErrors.RowFailed => "No fue posible crear la invitación. Reintenta esta fila más tarde.",
        InvitationScopeReasons.RoleNotFound => "El rol no existe o no está disponible para este destino.",
        InvitationScopeReasons.RoleAmbiguous => "El rol coincide con más de un rol del catálogo; usa el código del rol.",
        InvitationScopeReasons.SuperAdminRoleNotAssignable =>
            "El rol Super Administrador pertenece al perfil FLIT y no puede asignarse desde una compañía ni desde un organismo de tránsito.",
        InvitationScopeReasons.RoleTargetEntityTypeMismatch => "El rol no aplica al tipo de entidad del destino.",
        InvitationScopeReasons.AdminRoleNotAssignable => "Solo un administrador puede invitar a otro administrador.",
        _ => "La fila no es válida.",
    };

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[bulk-invite] Row {RowNumber} failed unexpectedly ({ExceptionType}); the batch continues.")]
    private static partial void LogRowFailed(ILogger logger, int rowNumber, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[bulk-invite] Commit deadline of {Seconds}s reached; remaining rows reported as not_processed.")]
    private static partial void LogDeadlineReached(ILogger logger, int seconds);
}

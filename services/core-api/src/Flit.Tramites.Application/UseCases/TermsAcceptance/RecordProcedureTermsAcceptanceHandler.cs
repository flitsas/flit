using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.TermsAcceptance;

namespace Flit.Tramites.Application.UseCases.TermsAcceptance;

/// <summary>
/// Documento de Términos y Condiciones que acepta el usuario antes de crear un trámite (Epic
/// #12543). La URL se puede sobreescribir por configuración (<c>ProcedureTerms:Url</c>); el
/// valor por defecto es la página pública de FLIT 1, que es el documento vigente hoy.
/// </summary>
public sealed class ProcedureTermsOptions
{
    public const string SectionName = "ProcedureTerms";

    /// <summary>Página pública con el documento completo, la misma que abre el enlace del checkbox.</summary>
    public const string DefaultUrl = "https://www.flitsas.com/privacyPolicy/public";

    public string Url { get; set; } = DefaultUrl;
}

/// <summary>Lo que el endpoint sabe de la petición: quién, desde dónde y para qué tipo de trámite.</summary>
public sealed record RecordProcedureTermsAcceptanceCommand(
    Guid? TenantId,
    Guid UserId,
    string ProcedureTypeCode,
    string? ClientIp,
    string? UserAgent);

public enum RecordProcedureTermsAcceptanceOutcome
{
    Recorded,
    /// <summary>El body no trae un code utilizable (vacío o demasiado largo).</summary>
    InvalidProcedureTypeCode,
    /// <summary>El code no corresponde a ningún tipo de trámite del catálogo.</summary>
    ProcedureTypeNotFound,
}

public sealed record RecordProcedureTermsAcceptanceResult(
    RecordProcedureTermsAcceptanceOutcome Outcome,
    ProcedureTermsAcceptance? Acceptance = null);

/// <summary>
/// Persistencia de la aceptación. La implementación DEBE propagar el fallo: si la fila no queda
/// escrita, el endpoint responde error y el frontend no habilita el formulario (RN-03).
/// </summary>
public interface IProcedureTermsAcceptanceRepository
{
    Task AddAsync(ProcedureTermsAcceptance acceptance, CancellationToken ct = default);
}

/// <summary>
/// Reflejo de la aceptación en el rastro administrativo unificado (<c>IAdminAuditWriter</c> →
/// <c>admin.tenant_config_audit_logs</c>), para que se vea en la pantalla de Auditoría del
/// SuperAdmin. Best-effort por diseño de ese writer: la evidencia real es la fila de
/// <see cref="IProcedureTermsAcceptanceRepository"/>.
/// </summary>
public interface IProcedureTermsAcceptanceAuditWriter
{
    Task WriteAcceptedAsync(ProcedureTermsAcceptance acceptance, CancellationToken ct = default);
}

/// <summary>
/// Registra que el usuario aceptó los T&amp;C para crear un trámite de un tipo concreto (Epic
/// #12543, §5). Valida que el tipo exista en el catálogo —no se guarda evidencia de un
/// <c>tramite_type</c> inventado— y escribe primero la fila propia (hard-fail) y después el
/// reflejo en el rastro unificado.
/// </summary>
public sealed class RecordProcedureTermsAcceptanceHandler(
    IProcedureTermsAcceptanceRepository repository,
    IProcedureTermsAcceptanceAuditWriter auditWriter,
    IProcedureTypeRepository procedureTypes,
    ProcedureTermsOptions options)
{
    /// <summary>Largo de <c>tramites.procedure_types.code</c>.</summary>
    public const int MaxProcedureTypeCodeLength = 50;

    public async Task<RecordProcedureTermsAcceptanceResult> HandleAsync(
        RecordProcedureTermsAcceptanceCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = command.ProcedureTypeCode?.Trim() ?? string.Empty;
        if (code.Length is 0 or > MaxProcedureTypeCodeLength)
            return new(RecordProcedureTermsAcceptanceOutcome.InvalidProcedureTypeCode);

        if (!await procedureTypes.CodeExistsAsync(code, ct).ConfigureAwait(false))
            return new(RecordProcedureTermsAcceptanceOutcome.ProcedureTypeNotFound);

        var acceptance = new ProcedureTermsAcceptance
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            UserId = command.UserId,
            ProcedureTypeCode = code,
            TermsUrl = options.Url,
            AcceptedAt = DateTimeOffset.UtcNow,
            ClientIp = command.ClientIp,
            UserAgent = command.UserAgent,
        };

        await repository.AddAsync(acceptance, ct).ConfigureAwait(false);
        await auditWriter.WriteAcceptedAsync(acceptance, ct).ConfigureAwait(false);

        return new(RecordProcedureTermsAcceptanceOutcome.Recorded, acceptance);
    }
}

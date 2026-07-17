using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.SetOtConsultationRestriction;

/// <summary>
/// Caso de uso de fijar el estado deseado (habilitada/inhabilitada) de una consulta por OT
/// para un tenant (HU #10759).
///
/// Flujo: (1) valida que <c>transitOfficeId</c> exista en el catálogo — si no, 422 sin tocar
/// BD; (2) valida que el OT esté HABILITADO para la compañía (grant activo): restringir un
/// OT sin grant no significa nada, así que se exige habilitarlo primero (AC3); (3) valida
/// que <c>consultationKind</c> sea uno de los restringibles (AC4); (4) delega al repositorio
/// el upsert idempotente + audit en una única transacción (AC1/AC2).
/// </summary>
public sealed class SetOtConsultationRestrictionHandler
{
    /// <summary>Mensaje 422 cuando el OT no existe en el catálogo (AC3).</summary>
    public const string TransitOfficeNotFoundMessage =
        "El organismo de tránsito no existe en el catálogo.";

    /// <summary>Mensaje 422 cuando el OT no está habilitado (grant) para la compañía (AC3).</summary>
    public const string TransitOfficeNotGrantedMessage =
        "Este organismo no está habilitado para la compañía. Habilítelo primero en los "
        + "Organismos de Tránsito de la compañía antes de restringir sus consultas.";

    /// <summary>Mensaje 422 cuando el kind no es restringible (AC4).</summary>
    public const string InvalidConsultationKindMessage =
        "El tipo de consulta no es restringible. Valores permitidos: rnmc, fines.";

    private readonly ITransitOfficeCatalog _catalog;
    private readonly ITransitGrantRepository _grants;
    private readonly IOtConsultationRestrictionRepository _repository;

    public SetOtConsultationRestrictionHandler(
        ITransitOfficeCatalog catalog,
        ITransitGrantRepository grants,
        IOtConsultationRestrictionRepository repository)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<SetOtConsultationRestrictionResult> HandleAsync(
        SetOtConsultationRestrictionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // AC3 (1/2): el organismo debe existir en el catálogo.
        if (command.TransitOfficeId == Guid.Empty || !_catalog.Exists(command.TransitOfficeId))
        {
            return Invalid(
                "transitOfficeId",
                TransitOfficeNotFoundMessage,
                command.TransitOfficeId == Guid.Empty ? null : command.TransitOfficeId.ToString());
        }

        // AC3 (2/2): el organismo debe estar habilitado (grant) para la compañía — restringir
        // un OT sin grant no significa nada.
        var enabledOffices = await _grants
            .ListEnabledOfficeIdsAsync(command.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (!enabledOffices.Contains(command.TransitOfficeId))
        {
            return Invalid(
                "transitOfficeId", TransitOfficeNotGrantedMessage, command.TransitOfficeId.ToString());
        }

        // AC4: el kind debe ser uno de los restringibles ("vehicle" u otros valores no
        // configurables quedan fuera del CHECK a propósito).
        if (!RestrictedConsultationKinds.IsValid(command.ConsultationKind))
        {
            return Invalid("consultationKind", InvalidConsultationKindMessage, command.ConsultationKind);
        }

        // AC1/AC2: upsert idempotente + audit en una única transacción.
        await _repository
            .SetAsync(
                command.TenantId,
                command.TransitOfficeId,
                command.ConsultationKind!,
                command.Enabled,
                command.ChangedBy,
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        return SetOtConsultationRestrictionResult.Success();
    }

    private static SetOtConsultationRestrictionResult Invalid(string field, string message, string? value) =>
        SetOtConsultationRestrictionResult.Invalid(
        [
            new OtConsultationRestrictionValidationError(field, message, value),
        ]);
}

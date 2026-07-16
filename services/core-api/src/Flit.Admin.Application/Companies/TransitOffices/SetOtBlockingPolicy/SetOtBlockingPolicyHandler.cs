using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.SetOtBlockingPolicy;

/// <summary>
/// Caso de uso de fijar el estado deseado (bloquea/advierte) de un criterio del preflight por OT
/// para un tenant (FEATURE 05).
///
/// Flujo (paridad con SetOtConsultationRestriction): (1) valida que <c>transitOfficeId</c> exista en
/// el catálogo — si no, 422 sin tocar BD; (2) valida que el OT esté HABILITADO para la compañía
/// (grant activo): configurar un OT sin grant no significa nada (AC3); (3) valida que
/// <c>criterion</c> sea uno de los configurables (AC4); (4) delega al repositorio el upsert
/// idempotente + audit en una única transacción (AC1/AC2).
/// </summary>
public sealed class SetOtBlockingPolicyHandler
{
    /// <summary>Mensaje 422 cuando el OT no existe en el catálogo (AC3).</summary>
    public const string TransitOfficeNotFoundMessage =
        "El organismo de tránsito no existe en el catálogo.";

    /// <summary>Mensaje 422 cuando el OT no está habilitado (grant) para la compañía (AC3).</summary>
    public const string TransitOfficeNotGrantedMessage =
        "Este organismo no está habilitado para la compañía. Habilítelo primero en los "
        + "Organismos de Tránsito de la compañía antes de configurar sus criterios de bloqueo.";

    /// <summary>Mensaje 422 cuando el criterio no es configurable (AC4).</summary>
    public const string InvalidCriterionMessage =
        "El criterio no es configurable. Valores permitidos: soat, rtm, estado_vehiculo, fines, rnmc.";

    private readonly ITransitOfficeCatalog _catalog;
    private readonly ITransitGrantRepository _grants;
    private readonly IOtBlockingPolicyRepository _repository;

    public SetOtBlockingPolicyHandler(
        ITransitOfficeCatalog catalog,
        ITransitGrantRepository grants,
        IOtBlockingPolicyRepository repository)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<SetOtBlockingPolicyResult> HandleAsync(
        SetOtBlockingPolicyCommand command,
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

        // AC3 (2/2): el organismo debe estar habilitado (grant) para la compañía.
        var enabledOffices = await _grants
            .ListEnabledOfficeIdsAsync(command.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (!enabledOffices.Contains(command.TransitOfficeId))
        {
            return Invalid(
                "transitOfficeId", TransitOfficeNotGrantedMessage, command.TransitOfficeId.ToString());
        }

        // AC4: el criterio debe ser uno de los configurables (CHECK cerrado en BD).
        if (!BlockingCriteria.IsValid(command.Criterion))
        {
            return Invalid("criterion", InvalidCriterionMessage, command.Criterion);
        }

        // AC1/AC2: upsert idempotente + audit en una única transacción.
        await _repository
            .SetAsync(
                command.TenantId,
                command.TransitOfficeId,
                command.Criterion!,
                command.Blocks,
                command.ChangedBy,
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        return SetOtBlockingPolicyResult.Success();
    }

    private static SetOtBlockingPolicyResult Invalid(string field, string message, string? value) =>
        SetOtBlockingPolicyResult.Invalid(
        [
            new OtBlockingPolicyValidationError(field, message, value),
        ]);
}

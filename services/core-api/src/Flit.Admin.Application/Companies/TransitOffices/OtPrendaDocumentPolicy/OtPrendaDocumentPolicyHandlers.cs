using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.OtPrendaDocumentPolicy;

public sealed record OtPrendaDocumentPolicyResponse(Guid TransitOfficeId, bool DocumentOptional);

public sealed record OtPrendaDocumentPolicyCompanyResponse(
    Guid TenantId,
    string TenantName,
    bool DocumentOptional);

public sealed record SetOtPrendaDocumentPolicyRequest(bool DocumentOptional);

public sealed class SetOtPrendaDocumentPolicyCommand
{
    public Guid TenantId { get; init; }
    public Guid TransitOfficeId { get; init; }
    public bool DocumentOptional { get; init; }
    public Guid? ChangedBy { get; init; }
    public Guid? CorrelationId { get; init; }
}

public sealed record OtPrendaDocumentPolicyValidationError(string Field, string Message, string? Value);

public sealed class SetOtPrendaDocumentPolicyResult
{
    public bool IsValid { get; private init; }
    public IReadOnlyList<OtPrendaDocumentPolicyValidationError> Errors { get; private init; } = [];

    public static SetOtPrendaDocumentPolicyResult Success() => new() { IsValid = true };

    public static SetOtPrendaDocumentPolicyResult Invalid(
        IReadOnlyList<OtPrendaDocumentPolicyValidationError> errors) =>
        new() { IsValid = false, Errors = errors };
}

public sealed class SetOtPrendaDocumentPolicyHandler
{
    public const string TransitOfficeNotFoundMessage =
        "El organismo de tránsito no existe en el catálogo.";

    public const string TransitOfficeNotGrantedMessage =
        "Este organismo no está habilitado para la compañía. Habilítelo primero antes de configurar la prenda.";

    private readonly ITransitOfficeCatalog _catalog;
    private readonly ITransitGrantRepository _grants;
    private readonly IOtPrendaDocumentPolicyRepository _repository;

    public SetOtPrendaDocumentPolicyHandler(
        ITransitOfficeCatalog catalog,
        ITransitGrantRepository grants,
        IOtPrendaDocumentPolicyRepository repository)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<SetOtPrendaDocumentPolicyResult> HandleAsync(
        SetOtPrendaDocumentPolicyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.TransitOfficeId == Guid.Empty || !_catalog.Exists(command.TransitOfficeId))
        {
            return SetOtPrendaDocumentPolicyResult.Invalid(
            [
                new OtPrendaDocumentPolicyValidationError(
                    "transitOfficeId",
                    TransitOfficeNotFoundMessage,
                    command.TransitOfficeId == Guid.Empty ? null : command.TransitOfficeId.ToString()),
            ]);
        }

        var enabledOffices = await _grants
            .ListEnabledOfficeIdsAsync(command.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (!enabledOffices.Contains(command.TransitOfficeId))
        {
            return SetOtPrendaDocumentPolicyResult.Invalid(
            [
                new OtPrendaDocumentPolicyValidationError(
                    "transitOfficeId",
                    TransitOfficeNotGrantedMessage,
                    command.TransitOfficeId.ToString()),
            ]);
        }

        await _repository
            .SetAsync(
                command.TenantId,
                command.TransitOfficeId,
                command.DocumentOptional,
                command.ChangedBy,
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        return SetOtPrendaDocumentPolicyResult.Success();
    }
}

public sealed class GetOtPrendaDocumentPoliciesHandler
{
    private readonly IOtPrendaDocumentPolicyRepository _repository;

    public GetOtPrendaDocumentPoliciesHandler(IOtPrendaDocumentPolicyRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<OtPrendaDocumentPolicyResponse>> HandleAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var items = await _repository.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return [.. items.Select(i => new OtPrendaDocumentPolicyResponse(i.TransitOfficeId, i.DocumentOptional))];
    }

    public async Task<IReadOnlyList<OtPrendaDocumentPolicyCompanyResponse>> HandleForOfficeAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var items = await _repository
            .ListCompaniesForOfficeAsync(transitOfficeId, cancellationToken)
            .ConfigureAwait(false);
        return [.. items.Select(i =>
            new OtPrendaDocumentPolicyCompanyResponse(i.TenantId, i.TenantName, i.DocumentOptional))];
    }
}

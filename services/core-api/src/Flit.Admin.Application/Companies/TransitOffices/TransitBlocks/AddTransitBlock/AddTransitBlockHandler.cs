using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Application.Companies.TransitOffices.TransitBlocks;

namespace Flit.Admin.Application.Companies.TransitOffices.TransitBlocks.AddTransitBlock;

/// <summary>
/// Alta de bloqueo de OT para cabeza Marca Blanca (HU #12407 AC1/AC3).
/// </summary>
public sealed class AddTransitBlockHandler
{
    public const string SoloMarcaBlancaMessage =
        "Los bloqueos de organismos de tránsito solo aplican a compañías Marca Blanca.";

    public const string OtNoRegistradoMessage =
        "El organismo de tránsito no existe en el catálogo de la plataforma.";

    public const string OtNoOperativoMessage =
        "Este organismo no está operativo en FLIT. Solo se pueden bloquear OT registrados y activos.";

    private readonly ICompanyHierarchyRepository _hierarchy;
    private readonly ITransitOfficeCatalog _catalog;
    private readonly ITransitOfficeOperationalStatusReader _operationalStatus;
    private readonly ITenantTransitOfficeBlockRepository _repository;

    public AddTransitBlockHandler(
        ICompanyHierarchyRepository hierarchy,
        ITransitOfficeCatalog catalog,
        ITransitOfficeOperationalStatusReader operationalStatus,
        ITenantTransitOfficeBlockRepository repository)
    {
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _operationalStatus = operationalStatus ?? throw new ArgumentNullException(nameof(operationalStatus));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<AddTransitBlockResult> HandleAsync(
        AddTransitBlockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var head = await _hierarchy
            .GetHierarchyInfoAsync(command.HeadTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (head is null)
        {
            return Invalid("headTenantId", "La compañía no existe.", command.HeadTenantId.ToString());
        }

        if (!string.Equals(head.TenantType, CompanyTenantTypes.MarcaBlanca, StringComparison.Ordinal)
            || head.ParentTenantId is not null)
        {
            return Invalid("headTenantId", SoloMarcaBlancaMessage, command.HeadTenantId.ToString());
        }

        if (command.TransitOfficeId == Guid.Empty || !_catalog.Exists(command.TransitOfficeId))
        {
            return Invalid("transitOfficeId", OtNoRegistradoMessage, command.TransitOfficeId.ToString());
        }

        var status = await _operationalStatus
            .GetByIdAsync(command.TransitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (status is null || !status.HasTenant || status.EstadoActivo != true)
        {
            return Invalid("transitOfficeId", OtNoOperativoMessage, command.TransitOfficeId.ToString());
        }

        var added = await _repository
            .AddBlockAsync(
                command.HeadTenantId,
                command.TransitOfficeId,
                command.CreatedBy,
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        return AddTransitBlockResult.Success(added);
    }

    private static AddTransitBlockResult Invalid(string field, string message, string? value) =>
        AddTransitBlockResult.Invalid([new TransitBlockValidationError(field, message, value)]);
}

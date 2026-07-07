using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.AddTransitGrant;

/// <summary>
/// Caso de uso del alta de un grant de organismo de tránsito (HU #10192 AC2, HU #10518).
///
/// Flujo: (1) valida que el <c>transitOfficeId</c> exista en el catálogo — si no, 422 sin
/// tocar BD; (2) valida que el OT sea OPERATIVO en FLIT (tenant OT dado de alta y activo):
/// no se puede habilitar un OT sin alta ni inactivo para una compañía (HU #10518); (3)
/// delega al repositorio el alta + audit en una única transacción. Idempotente: si el grant
/// ya existe no duplica fila ni auditoría (<see cref="AddTransitGrantResult.Added"/> = false).
/// Quitar el grant (<c>RemoveTransitGrantHandler</c>) no tiene esta restricción.
/// </summary>
public sealed class AddTransitGrantHandler
{
    /// <summary>Mensaje 422 cuando el OT no tiene tenant OT dado de alta en FLIT (ot_sin_alta).</summary>
    public const string SinAltaMessage =
        "Este organismo no está dado de alta en FLIT. Actívelo primero en Administración de organismos de tránsito.";

    /// <summary>Mensaje 422 cuando el OT tiene tenant pero está inactivo (ot_inactivo).</summary>
    public const string InactivoMessage =
        "Este organismo está inactivo en FLIT. Actívelo antes de habilitarlo para la compañía.";

    private readonly ITransitOfficeCatalog _catalog;
    private readonly ITransitOfficeOperationalStatusReader _operationalStatus;
    private readonly ITransitGrantRepository _repository;

    public AddTransitGrantHandler(
        ITransitOfficeCatalog catalog,
        ITransitOfficeOperationalStatusReader operationalStatus,
        ITransitGrantRepository repository)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _operationalStatus = operationalStatus ?? throw new ArgumentNullException(nameof(operationalStatus));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<AddTransitGrantResult> HandleAsync(
        AddTransitGrantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // AC2: el organismo debe existir en el catálogo.
        if (command.TransitOfficeId == Guid.Empty || !_catalog.Exists(command.TransitOfficeId))
        {
            return AddTransitGrantResult.Invalid(
            [
                new TransitGrantValidationError(
                    "transitOfficeId",
                    "El organismo de tránsito no existe en el catálogo.",
                    command.TransitOfficeId == Guid.Empty ? null : command.TransitOfficeId.ToString()),
            ]);
        }

        // HU #10518: no se puede habilitar un OT que no esté operativo en FLIT.
        var status = await _operationalStatus
            .GetByIdAsync(command.TransitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (status is null || !status.HasTenant)
        {
            return InvalidOperability(command.TransitOfficeId, SinAltaMessage);
        }

        if (status.EstadoActivo != true)
        {
            return InvalidOperability(command.TransitOfficeId, InactivoMessage);
        }

        var added = await _repository
            .AddGrantAsync(
                command.TenantId, command.TransitOfficeId, command.CreatedBy, command.CorrelationId, cancellationToken)
            .ConfigureAwait(false);

        return AddTransitGrantResult.Success(added);
    }

    private static AddTransitGrantResult InvalidOperability(Guid transitOfficeId, string message) =>
        AddTransitGrantResult.Invalid(
        [
            new TransitGrantValidationError("transitOfficeId", message, transitOfficeId.ToString()),
        ]);
}

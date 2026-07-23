namespace Flit.Ict.Application.Edit;

/// <summary>
/// Edición parcial de un pre-trámite. No editables: llaves de correlación (placa/VIN),
/// transaction_type y tenant. Requiere el <see cref="RowVersion"/> actual (concurrencia optimista).
/// </summary>
public sealed record EditPreTramiteCommand(
    Guid Id,
    long RowVersion,
    string? DeliveryAddress = null,
    string? ManagerMail = null,
    string? SellingDate = null,
    decimal? SellingPrice = null,
    string? TrafficSecretaryCode = null,
    bool? ProcessWithoutAttachedDocuments = null);

public sealed record EditPreTramiteResult(Guid Id, bool ValidationReset);

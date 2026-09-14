namespace Flit.Admin.Application.Companies.TransitOffices.TransitBlocks.RemoveTransitBlock;

public sealed record RemoveTransitBlockCommand(
    Guid HeadTenantId,
    Guid TransitOfficeId,
    Guid? ChangedBy,
    Guid? CorrelationId = null);

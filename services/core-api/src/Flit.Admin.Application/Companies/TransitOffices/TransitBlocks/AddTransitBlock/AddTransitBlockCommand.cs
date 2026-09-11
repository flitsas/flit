namespace Flit.Admin.Application.Companies.TransitOffices.TransitBlocks.AddTransitBlock;

public sealed record AddTransitBlockCommand(
    Guid HeadTenantId,
    Guid TransitOfficeId,
    Guid? CreatedBy,
    Guid? CorrelationId = null);

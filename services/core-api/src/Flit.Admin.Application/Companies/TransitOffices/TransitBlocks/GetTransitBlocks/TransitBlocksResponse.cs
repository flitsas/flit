namespace Flit.Admin.Application.Companies.TransitOffices.TransitBlocks.GetTransitBlocks;

public sealed record TransitBlocksResponse(IReadOnlyList<Guid> TransitOfficeIds);

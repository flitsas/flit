namespace Flit.Modules.Security.Application.Modules;

public sealed record UpdateModuleCommand(Guid Id, string Name, string? Description, short SortOrder);

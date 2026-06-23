namespace Flit.Modules.Security.Application.Modules;

public sealed record CreateModuleCommand(string Code, string Name, string? Description, short SortOrder);

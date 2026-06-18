namespace Flit.Tramites.Domain.ValueObjects;

public sealed record ValidationError(string Code, string Message, string Path);

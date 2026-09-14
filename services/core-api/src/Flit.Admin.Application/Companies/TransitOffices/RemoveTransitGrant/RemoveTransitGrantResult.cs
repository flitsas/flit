namespace Flit.Admin.Application.Companies.TransitOffices.RemoveTransitGrant;

public sealed class RemoveTransitGrantResult
{
    private RemoveTransitGrantResult(bool removed, bool denied, string? message)
    {
        Removed = removed;
        Denied = denied;
        Message = message;
    }

    public bool Removed { get; }

    public bool Denied { get; }

    public string? Message { get; }

    public static RemoveTransitGrantResult Success() => new(true, false, null);

    public static RemoveTransitGrantResult NotFound() => new(false, false, null);

    public static RemoveTransitGrantResult BusinessDenied(string message) => new(false, true, message);
}

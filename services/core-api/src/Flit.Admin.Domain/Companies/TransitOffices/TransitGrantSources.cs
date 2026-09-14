namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>Origen de una habilitación OT↔compañía (HU #12346).</summary>
public static class TransitGrantSources
{
    public const string Client = "CLIENT";

    public const string System = "SYSTEM";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Client,
        System,
    };

    public static bool IsValid(string? value) =>
        value is not null && All.Contains(value);
}

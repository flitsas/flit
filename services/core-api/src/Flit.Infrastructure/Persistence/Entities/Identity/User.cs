namespace Flit.Infrastructure.Persistence.Entities.Identity;

public sealed class User : Entities.Common.AuditableEntity
{
    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Status { get; set; } = "pending";

    /// <summary>Identificador alterno (número de documento) para "recordar usuario" (HU #10203).</summary>
    public string? DocumentNumber { get; set; }
}
